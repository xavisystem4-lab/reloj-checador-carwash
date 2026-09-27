using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Payroll;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.EmployeeDeviceMappings;
using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Cloud;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// Une un <see cref="Employee"/> con el nombre de su sucursal y un resumen de sus
/// vínculos a dispositivos, ambos ya resueltos — Employee solo guarda BranchId (Guid) sin
/// navegación a Branch, y sus vínculos viven en una tabla aparte
/// (EmployeeDeviceMapping), así que el DataGrid bindea a esto en vez de al Employee crudo.
///
/// <see cref="PinSummary"/> (pedido explícito del usuario: "quiero que me aparezca en el
/// módulo de empleados el PIN") es el mismo dato que ya vivía embebido dentro de
/// <see cref="LinkedDevicesSummary"/> (p. ej. "Checador (PIN 12)") pero como columna propia
/// — útil para comparar de un vistazo contra el PIN de un CSV de "Reemplazar catálogo" sin
/// tener que leerlo dentro del texto del dispositivo. Vacío ("") si no tiene ningún vínculo
/// todavía, nunca "Sin vincular" — ese texto ya lo cubre LinkedDevicesSummary, repetirlo en
/// esta columna solo generaría ruido visual en una tabla con muchas filas.</summary>
public sealed record EmployeeRow(Employee Employee, string BranchName, string LinkedDevicesSummary, string PinSummary)
{
    /// <summary>Clave de orden NUMÉRICA para la columna "Número" — pedido explícito del
    /// usuario ("acomodar por Número de mayor a menor"): Employee.Number es texto (soporta
    /// formatos viejos como "EMP-001"), así que el orden por defecto del DataGrid lo
    /// compararía como texto ("10" antes que "2") en vez de como número real. La columna usa
    /// esto como SortMemberPath mientras sigue MOSTRANDO el texto real (Employee.Number). Un
    /// número que no se pueda parsear (formato viejo tipo "EMP-001") cae al final
    /// (int.MaxValue) en vez de romper el orden de los que sí son numéricos.</summary>
    public int NumberSortKey => int.TryParse(Employee.Number.Value, out var n) ? n : int.MaxValue;

    /// <summary>"08:00 - 16:00", o "Sin capturar" — pedido explícito del usuario: "que en
    /// Empleados me aparezcan sus horarios". Employee.UpdateSchedule nunca deja uno solo
    /// de los dos campos capturado, así que basta revisar ScheduledStartTime.</summary>
    public string ScheduleSummary => Employee.ScheduledStartTime is { } start && Employee.ScheduledEndTime is { } end
        ? $"{start:HH\\:mm} - {end:HH\\:mm}"
        : "Sin capturar";

    /// <summary>"⭐ Horario especial" cuando aplica, para verlo de un vistazo en la grilla
    /// junto al horario — ver Employee.HasSpecialSchedule.</summary>
    public string ScheduleSummaryWithSpecialFlag => Employee.HasSpecialSchedule
        ? $"{ScheduleSummary} ⭐ Especial"
        : ScheduleSummary;
}

/// <summary>Un vínculo de un empleado a un dispositivo, con el nombre del dispositivo ya
/// resuelto — usado por EditEmployeeMappingsDialog para poder corregir el PIN sin
/// necesitar el objeto Device completo.</summary>
public sealed record EmployeeMappingInfo(Guid MappingId, string DeviceName, string DeviceUserPin);

/// <summary>
/// ViewModel de la pantalla de Empleados: alta (con sueldo semanal/tarifa de hora extra,
/// insumo de nómina sin cálculo fiscal — ver Employee.cs), edición, baja lógica
/// ("eliminar" = ChangeStatus a Terminated, nunca se borra el registro — ver
/// DeleteEmployeeAsync) y listado (Fase 3), más el vínculo
/// Empleado↔Dispositivo (EmployeeDeviceMapping) — asocia el PIN interno que cada reloj usa
/// para reconocer a un empleado, prerequisito para que una futura pantalla de Asistencia
/// pueda mostrar nombres en vez de PINs crudos. El PIN se captura a mano en el diálogo
/// (LinkEmployeeDeviceDialog), no se descarga del dispositivo conectado — decisión de
/// alcance explícita para esta entrega. Al crear un vínculo, se concilian retroactivamente
/// las marcaciones de ese dispositivo+PIN que hayan llegado antes de que existiera (ver
/// ReconcileAttendancesAsync) — sin esto quedarían para siempre como "sin vincular".
///
/// Tras cualquier alta/edición/vínculo, se recarga la lista COMPLETA desde la base local
/// (ReloadAsync) en vez de mutar Employees a mano (Add/reemplazo puntual, como se hacía
/// antes) — se reporta un caso real en Windows donde el DataGrid no reflejaba la fila
/// recién agregada hasta reiniciar la app (los datos sí quedaban guardados y sincronizados
/// a Supabase correctamente; era un problema de refresco de la vista, no de persistencia).
/// Recargar todo es más lento en teoría, pero con el volumen de un negocio de una sola
/// sucursal-tipo es instantáneo, y elimina de raíz cualquier posible desincronización
/// entre el estado en memoria y la base de datos real.
/// </summary>
public sealed partial class EmployeesViewModel : ObservableObject
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IPayrollDeductionRepository _payrollDeductionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SupabaseSyncBackgroundService _syncService;

    private IReadOnlyList<EmployeeRow> _allRows = [];

    [ObservableProperty]
    private string _statusMessage = "Cargando empleados...";

    /// <summary>"Eliminar" un empleado es una baja lógica (ChangeStatus a Terminated, ver
    /// DeleteEmployeeAsync) — nunca se borra el registro ni su historial. Por defecto los
    /// dados de baja se ocultan de la lista; este toggle los vuelve a mostrar sin
    /// necesidad de recargar la base de datos otra vez (filtra en memoria sobre
    /// _allRows).</summary>
    [ObservableProperty]
    private bool _showTerminatedEmployees;

    /// <summary>Texto libre de búsqueda (nombre o número de empleado), pedido para poder
    /// navegar el catálogo real de 54+ empleados sin desplazarse a mano por todo el
    /// DataGrid — filtra en memoria sobre _allRows, igual criterio que ShowTerminatedEmployees.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>"Todas las sucursales" siempre es la primera opción — se reconstruye en
    /// cada ReloadAsync a partir de las sucursales que realmente tienen empleados, nunca
    /// de la lista completa de Sucursales (evitaría mostrar una sucursal vacía como
    /// filtro útil).</summary>
    public ObservableCollection<string> BranchFilterOptions { get; } = [AllBranchesOption];

    [ObservableProperty]
    private string _selectedBranchFilter = AllBranchesOption;

    public static readonly string[] StatusFilterOptions =
        [AllStatusesOption, "Activo", "De permiso", "Inactivo", "Baja"];

    [ObservableProperty]
    private string _selectedStatusFilter = AllStatusesOption;

    private const string AllBranchesOption = "Todas las sucursales";
    private const string AllStatusesOption = "Todos los estatus";

    public ObservableCollection<EmployeeRow> Employees { get; } = [];

    public EmployeesViewModel(
        IEmployeeRepository employeeRepository, IBranchRepository branchRepository, IDeviceRepository deviceRepository,
        IEmployeeDeviceMappingRepository mappingRepository, IAttendanceRepository attendanceRepository,
        IPayrollDeductionRepository payrollDeductionRepository, IUnitOfWork unitOfWork,
        SupabaseSyncBackgroundService syncService)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _mappingRepository = mappingRepository;
        _attendanceRepository = attendanceRepository;
        _payrollDeductionRepository = payrollDeductionRepository;
        _unitOfWork = unitOfWork;
        _syncService = syncService;
    }

    /// <summary>Carga inicial de la pantalla, y también lo que llama el botón "🔄
    /// Actualizar" — pedido explícito del usuario: cambios hechos en OTRA pantalla que sí
    /// tocan la base (p. ej. "Renumerar PINs" en Dispositivos, que corrige
    /// EmployeeDeviceMapping directo) no se reflejan solos aquí, esta pantalla no se entera
    /// hasta que algo la haga recargar.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de empleados.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
    }

    /// <summary>Recarga Employees completo desde la base local — ver comentario de clase
    /// sobre por qué esto reemplazó la mutación puntual (Add/reemplazo por índice).</summary>
    private async Task ReloadAsync()
    {
        // Las cuatro listas se cargan completas (sin paginación, igual que
        // Sucursales/Dispositivos) y se cruzan en memoria: ni Employee tiene navegación a
        // Branch, ni hay una vía directa de Employee a sus dispositivos vinculados — ambas
        // se resuelven aquí, no en la vista.
        var employees = await _employeeRepository.ListAsync();
        var branches = await _branchRepository.ListAsync();
        var devices = await _deviceRepository.ListAsync();
        var mappings = await _mappingRepository.ListAsync();

        var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);
        var deviceNamesById = devices.ToDictionary(d => d.Id, d => d.Name);

        _allRows = employees.Select(employee => BuildRow(employee, branchNamesById, deviceNamesById, mappings)).ToList();

        // Reconstruye las opciones de sucursal a partir de quién tiene empleados de
        // verdad — si la sucursal seleccionada ya no existe entre ellas (p. ej. tras
        // borrar la única persona que tenía), vuelve sola a "Todas las sucursales" en vez
        // de quedar apuntando a un filtro que ya no aplica a nadie.
        var branchNamesWithEmployees = _allRows.Select(row => row.BranchName).Distinct().OrderBy(name => name).ToList();
        BranchFilterOptions.Clear();
        BranchFilterOptions.Add(AllBranchesOption);
        foreach (var name in branchNamesWithEmployees)
        {
            BranchFilterOptions.Add(name);
        }

        if (!BranchFilterOptions.Contains(SelectedBranchFilter))
        {
            SelectedBranchFilter = AllBranchesOption;
        }

        ApplyVisibilityFilter();
    }

    partial void OnShowTerminatedEmployeesChanged(bool value) => ApplyVisibilityFilter();
    partial void OnSearchTextChanged(string value) => ApplyVisibilityFilter();
    partial void OnSelectedBranchFilterChanged(string value) => ApplyVisibilityFilter();
    partial void OnSelectedStatusFilterChanged(string value) => ApplyVisibilityFilter();


    /// <summary>Aplica en cadena los cuatro filtros disponibles (dados de baja, búsqueda de
    /// texto, sucursal, estatus) sobre _allRows — todos en memoria, sin volver a tocar la
    /// base de datos, para que escribir en el buscador o cambiar un combo se sienta
    /// instantáneo.</summary>
    private void ApplyVisibilityFilter()
    {
        IEnumerable<EmployeeRow> visible = ShowTerminatedEmployees
            ? _allRows
            : _allRows.Where(row => row.Employee.Status != EmploymentStatus.Terminated);

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            visible = visible.Where(row =>
                row.Employee.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.Employee.Number.Value.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedBranchFilter != AllBranchesOption)
        {
            visible = visible.Where(row => row.BranchName == SelectedBranchFilter);
        }

        if (SelectedStatusFilter != AllStatusesOption)
        {
            var status = MapStatusFilter(SelectedStatusFilter);
            visible = visible.Where(row => row.Employee.Status == status);
        }

        var visibleList = visible.ToList();

        Employees.Clear();
        foreach (var row in visibleList)
        {
            Employees.Add(row);
        }

        var hiddenCount = _allRows.Count - visibleList.Count;
        RefreshStatusMessage(hiddenCount);
    }

    private static EmploymentStatus MapStatusFilter(string label) => label switch
    {
        "Activo" => EmploymentStatus.Active,
        "De permiso" => EmploymentStatus.OnLeave,
        "Inactivo" => EmploymentStatus.Inactive,
        "Baja" => EmploymentStatus.Terminated,
        _ => EmploymentStatus.Active,
    };

    private static EmployeeRow BuildRow(
        Employee employee, Dictionary<Guid, string> branchNamesById, Dictionary<Guid, string> deviceNamesById,
        IReadOnlyList<EmployeeDeviceMapping> mappings)
    {
        var branchName = branchNamesById.TryGetValue(employee.BranchId, out var name) ? name : "(sucursal desconocida)";
        var ownMappings = mappings.Where(m => m.EmployeeId == employee.Id).ToList();
        var linkedDevicesSummary = BuildLinkedDevicesSummary(deviceNamesById, ownMappings);
        var pinSummary = string.Join(", ", ownMappings.Select(m => m.DeviceUserPin));
        return new EmployeeRow(employee, branchName, linkedDevicesSummary, pinSummary);
    }

    private static string BuildLinkedDevicesSummary(
        Dictionary<Guid, string> deviceNamesById, IReadOnlyList<EmployeeDeviceMapping> ownMappings)
    {
        if (ownMappings.Count == 0)
        {
            return "Sin vincular";
        }

        var parts = ownMappings.Select(m =>
        {
            var deviceName = deviceNamesById.TryGetValue(m.DeviceId, out var name) ? name : "(dispositivo desconocido)";
            return $"{deviceName} (PIN {m.DeviceUserPin})";
        });
        return string.Join(", ", parts);
    }

    /// <summary>Concilia con <paramref name="employeeId"/> (Y SU SUCURSAL — ver comentario
    /// de clase de Attendance: la sucursal de una marcación siempre se deriva del empleado,
    /// nunca del dispositivo) las marcaciones de <paramref name="deviceId"/>+
    /// <paramref name="deviceUserPin"/> que llegaron ANTES de que existiera este vínculo
    /// (EmployeeId/BranchId todavía null, "pendientes de asignación") — usa
    /// Attendance.ReconcileEmployee. Se llama justo antes de SaveChangesAsync en los tres
    /// lugares donde se crea un EmployeeDeviceMapping, para que quede en la misma
    /// transacción que el vínculo — si algo falla al guardar, ninguna de las dos cosas
    /// queda a medias.</summary>
    private async Task ReconcileAttendancesAsync(Guid employeeId, Guid deviceId, string deviceUserPin)
    {
        var employee = await _employeeRepository.GetByIdAsync(employeeId);
        var unresolved = await _attendanceRepository.ListUnresolvedByDeviceAndPinAsync(deviceId, deviceUserPin);
        foreach (var attendance in unresolved)
        {
            attendance.ReconcileEmployee(employeeId, employee?.BranchId);
        }
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

    /// <summary>Solo relojes que NO están deshabilitados — pedido explícito del usuario tras
    /// no encontrar el reloj real ("Checador") entre 5 relojes de prueba en el combo de
    /// "Vincular a dispositivo" (mismo criterio ya aplicado en GetUnresolvedPinsAsync). Se
    /// usa en los tres flujos que ofrecen elegir un reloj para vincular a alguien (alta,
    /// edición, "Vincular a dispositivo" puntual) — nunca tendría sentido vincular a un
    /// reloj deshabilitado de todos modos.</summary>
    public async Task<IReadOnlyList<Device>> GetDevicesAsync() =>
        (await _deviceRepository.ListAsync()).Where(d => d.Status != DeviceStatus.Disabled).ToList();

    /// <summary>Empleados activos para el combo de "a quién vincular" en el diálogo de
    /// vinculación masiva — incluye "De permiso" (sigue trabajando, solo temporalmente
    /// ausente) pero no "Baja"/Terminated (ya no debería recibir marcaciones nuevas).</summary>
    public async Task<IReadOnlyList<Employee>> ListLinkableEmployeesAsync() =>
        (await _employeeRepository.ListAsync())
            .Where(e => e.Status != EmploymentStatus.Terminated)
            .OrderBy(e => e.FullName)
            .ToList();

    public sealed record UnresolvedPinRow(
        Guid DeviceId, string DeviceName, string DeviceUserPin, int AttendanceCount, DateTime FirstSeenUtc, DateTime LastSeenUtc);

    /// <summary>Un renglón por cada combinación (dispositivo, PIN) que todavía tiene
    /// marcaciones sin vincular a ningún empleado — pedido explícito del usuario: "vincular
    /// de manera masiva ... seleccionar todos o de 1 por 1". Agrupa
    /// IAttendanceRepository.ListUnresolvedAsync (todas, sin límite de fecha) por
    /// dispositivo+PIN para no repetir la misma fila una vez por cada marcación.</summary>
    public async Task<IReadOnlyList<UnresolvedPinRow>> GetUnresolvedPinsAsync()
    {
        const int maxUnresolvedRows = 5000;
        var unresolved = await _attendanceRepository.ListUnresolvedAsync(maxUnresolvedRows);
        var devices = await _deviceRepository.ListAsync();
        var deviceNamesById = devices.ToDictionary(d => d.Id, d => d.Name);

        // Solo relojes que NO están deshabilitados — pedido explícito del usuario tras ver
        // el mismo PIN repetido una vez por cada reloj de prueba ("Arabica Café", "CrisaTec",
        // "Plaza Sabo", "Otro", "2"): confirmó que son datos de prueba y no debían aparecer
        // aquí. Se filtra por Status en vez de por nombre de dispositivo a propósito — si
        // alguno de esos relojes se vuelve a habilitar de verdad más adelante, sus PINs
        // pendientes reaparecen solos, sin tener que tocar este código otra vez.
        var enabledDeviceIds = devices.Where(d => d.Status != DeviceStatus.Disabled).Select(d => d.Id).ToHashSet();

        return unresolved
            .Where(a => enabledDeviceIds.Contains(a.DeviceId))
            .GroupBy(a => (a.DeviceId, a.DeviceUserPin))
            .Select(g => new UnresolvedPinRow(
                g.Key.DeviceId,
                deviceNamesById.TryGetValue(g.Key.DeviceId, out var name) ? name : "(dispositivo desconocido)",
                g.Key.DeviceUserPin,
                g.Count(),
                g.Min(a => a.TimestampUtc),
                g.Max(a => a.TimestampUtc)))
            .OrderByDescending(r => r.LastSeenUtc)
            .ToList();
    }

    /// <param name="weeklySalary">Insumo de nómina sin cálculo fiscal — ver comentario de
    /// clase de Employee.</param>
    /// <param name="deviceId">Dispositivo a vincular en la misma operación, o null para
    /// no vincular ahora (se puede hacer después con "Vincular a dispositivo").</param>
    /// <param name="deviceUserPin">Requerido si <paramref name="deviceId"/> no es null.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateEmployeeAsync(
        string number, string fullName, Guid branchId, DateOnly hireDate, decimal? weeklySalary, string? department, string? position,
        decimal? overtimeHourlyRate = null, Guid? deviceId = null, string? deviceUserPin = null, string? notes = null,
        TimeOnly? scheduledStartTime = null, TimeOnly? scheduledEndTime = null, bool hasSpecialSchedule = false)
    {
        try
        {
            var employee = Employee.Create(
                EmployeeNumber.Create(number), fullName, branchId, hireDate, weeklySalary, department, position, overtimeHourlyRate,
                hasSpecialSchedule: hasSpecialSchedule);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                employee.UpdateNotes(notes);
            }
            if (scheduledStartTime is not null || scheduledEndTime is not null)
            {
                employee.UpdateSchedule(scheduledStartTime, scheduledEndTime);
            }
            await _employeeRepository.AddAsync(employee);

            // Alta + vínculo en la misma transacción (un solo SaveChangesAsync más abajo):
            // si el PIN ya está en uso, ninguno de los dos queda a medias.
            if (deviceId is not null && !string.IsNullOrWhiteSpace(deviceUserPin))
            {
                var mapping = EmployeeDeviceMapping.Create(employee.Id, deviceId.Value, deviceUserPin);
                await _mappingRepository.AddAsync(mapping);
                await ReconcileAttendancesAsync(employee.Id, deviceId.Value, deviceUserPin);
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (DomainException ex)
        {
            // Ej.: número/nombre vacíos — validación de negocio, mensaje ya comprensible.
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // Índice único de Employee.Number, o (si se vinculó a un dispositivo en la
            // misma operación) alguno de los dos índices únicos de EmployeeDeviceMapping
            // (ver EmployeeConfiguration/EmployeeDeviceMappingConfiguration) — se interpreta
            // sin inspeccionar el texto nativo del error de SQLite, que no es estable entre
            // versiones.
            Log.Warning(ex, "No se pudo guardar el empleado (Number={Number}, DeviceId={DeviceId})", number, deviceId);
            return deviceId is not null
                ? "No se pudo guardar: el número de empleado ya existe, o ese PIN ya está en uso en el dispositivo elegido."
                : "Ya existe un empleado con ese número.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear un empleado (Number={Number})", number);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <param name="number">Número de empleado — puede haber cambiado respecto al actual
    /// (corrección de un error de captura del alta, ver Employee.ChangeNumber).</param>
    /// <param name="weeklySalary">Insumo de nómina sin cálculo fiscal — ver comentario de
    /// clase de Employee.</param>
    /// <param name="deviceId">Dispositivo a vincular en la misma operación si el empleado
    /// todavía no tenía ninguno (ver EditEmployeeDialog) — null si no aplica.</param>
    /// <param name="deviceUserPin">Requerido si <paramref name="deviceId"/> no es null.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateEmployeeAsync(
        Guid employeeId, string number, string fullName, Guid branchId, string? department, string? position,
        string? phone, string? email, EmploymentStatus status, decimal? weeklySalary, decimal? overtimeHourlyRate = null,
        Guid? deviceId = null, string? deviceUserPin = null, string? notes = null,
        TimeOnly? scheduledStartTime = null, TimeOnly? scheduledEndTime = null, bool hasSpecialSchedule = false)
    {
        try
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null)
            {
                // No debería pasar en uso normal (la fila viene de una lista ya cargada de
                // la misma base), pero es defensivo: "eliminar" es baja lógica (ver
                // DeleteEmployeeAsync), el registro nunca se borra de verdad.
                return "No se encontró el empleado — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            if (employee.Number.Value != number)
            {
                employee.ChangeNumber(EmployeeNumber.Create(number));
            }

            employee.UpdatePersonalInfo(fullName, department, position);
            employee.UpdateContact(phone, email);
            employee.UpdateCompensation(weeklySalary, overtimeHourlyRate);
            employee.UpdateNotes(notes);
            employee.UpdateSchedule(scheduledStartTime, scheduledEndTime);
            employee.SetSpecialSchedule(hasSpecialSchedule);
            if (employee.BranchId != branchId)
            {
                employee.TransferToBranch(branchId);
            }
            if (employee.Status != status)
            {
                employee.ChangeStatus(status);
            }

            // Primer vínculo del empleado, capturado en el mismo formulario de edición —
            // mismo patrón que CreateEmployeeAsync; EditEmployeeDialog solo ofrece esto
            // cuando el empleado aún no tenía ningún dispositivo vinculado.
            if (deviceId is not null && !string.IsNullOrWhiteSpace(deviceUserPin))
            {
                var mapping = EmployeeDeviceMapping.Create(employeeId, deviceId.Value, deviceUserPin);
                await _mappingRepository.AddAsync(mapping);
                await ReconcileAttendancesAsync(employeeId, deviceId.Value, deviceUserPin);
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // Número duplicado (índice único de Employee.Number), o si se vinculó a un
            // dispositivo en la misma operación, alguno de los índices únicos de
            // EmployeeDeviceMapping — igual que el resto de la app, sin inspeccionar el
            // texto nativo del error de SQLite.
            Log.Warning(ex, "No se pudo editar el empleado (EmployeeId={EmployeeId}, Number={Number})", employeeId, number);
            return deviceId is not null
                ? "No se pudo guardar: el número de empleado ya está en uso, o ese PIN ya está en uso en el dispositivo elegido."
                : "Ya existe otro empleado con ese número.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al editar un empleado (EmployeeId={EmployeeId})", employeeId);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateMappingAsync(Guid employeeId, Guid deviceId, string deviceUserPin)
    {
        try
        {
            var mapping = EmployeeDeviceMapping.Create(employeeId, deviceId, deviceUserPin);
            await _mappingRepository.AddAsync(mapping);
            await ReconcileAttendancesAsync(employeeId, deviceId, deviceUserPin);
            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // Dos índices únicos posibles (ver EmployeeDeviceMappingConfiguration): mismo
            // empleado ya vinculado a este dispositivo, o mismo PIN ya usado en este
            // dispositivo por otro empleado — un solo mensaje cubre ambos casos, igual que
            // el resto de la app no distingue el texto nativo del error de SQLite.
            Log.Warning(ex, "No se pudo guardar el vínculo (EmployeeId={EmployeeId}, DeviceId={DeviceId}, Pin={Pin})",
                employeeId, deviceId, deviceUserPin);
            return "Ese PIN ya está en uso en este dispositivo, o este empleado ya está vinculado a él.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al vincular empleado a dispositivo (EmployeeId={EmployeeId}, DeviceId={DeviceId})",
                employeeId, deviceId);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <summary>Vínculos actuales del empleado, con nombre de dispositivo resuelto —
    /// usado por "Editar vínculo(s)" para poder corregir un PIN mal capturado (caso real:
    /// el usuario capturó el número de empleado en vez del PIN real del reloj, y no había
    /// forma de corregirlo — "vincular de nuevo" con el PIN correcto lo rechaza el índice
    /// único (DeviceId, EmployeeId), ver EmployeeDeviceMappingConfiguration).</summary>
    public async Task<IReadOnlyList<EmployeeMappingInfo>> GetMappingsForEmployeeAsync(Guid employeeId)
    {
        var mappings = await _mappingRepository.ListAsync();
        var devices = await _deviceRepository.ListAsync();
        var deviceNamesById = devices.ToDictionary(d => d.Id, d => d.Name);

        return mappings
            .Where(m => m.EmployeeId == employeeId)
            .Select(m => new EmployeeMappingInfo(
                m.Id, deviceNamesById.TryGetValue(m.DeviceId, out var name) ? name : "(dispositivo desconocido)", m.DeviceUserPin))
            .ToList();
    }

    /// <param name="newPinsByMappingId">Solo se tocan los vínculos cuyo PIN realmente
    /// cambió respecto al actual — evita un Touch/SaveChanges innecesario en los que no
    /// se editaron.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateMappingPinsAsync(IReadOnlyDictionary<Guid, string> newPinsByMappingId)
    {
        try
        {
            foreach (var (mappingId, newPin) in newPinsByMappingId)
            {
                var mapping = await _mappingRepository.GetByIdAsync(mappingId);
                if (mapping is null)
                {
                    // No debería pasar en uso normal (la lista viene de la misma base),
                    // pero defensivo ante la carrera de que alguien más lo haya tocado.
                    continue;
                }

                if (mapping.DeviceUserPin != newPin)
                {
                    mapping.UpdatePin(newPin);
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // Índice único (DeviceId, DeviceUserPin): el nuevo PIN ya está en uso por otro
            // empleado en ese mismo dispositivo.
            Log.Warning(ex, "No se pudo corregir el PIN de un vínculo.");
            return "Ese PIN ya está en uso en este dispositivo por otro empleado.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al corregir el PIN de un vínculo.");
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <summary>Borra uno o más vínculos Empleado↔Dispositivo — pedido explícito del
    /// usuario al ver un vínculo huérfano a un reloj de prueba deshabilitado (caso real:
    /// alguien vinculado a la vez a "Checador" y a un dispositivo "2" que no es real para
    /// el negocio). A diferencia de EmployeesViewModel.HardDeleteEmployeesAsync, esto NO
    /// borra al empleado ni desvincula sus marcaciones — solo quita el vínculo puntual;
    /// las marcaciones que ya se resolvieron por ese vínculo se quedan tal cual (registro de
    /// auditoría, nunca se tocan retroactivamente).</summary>
    public async Task<string?> RemoveMappingsAsync(IReadOnlyList<Guid> mappingIds)
    {
        try
        {
            foreach (var mappingId in mappingIds)
            {
                var mapping = await _mappingRepository.GetByIdAsync(mappingId);
                if (mapping is null)
                {
                    continue; // ya no existe (lista desactualizada) — se sigue con el resto
                }
                await _mappingRepository.RemoveAsync(mapping);
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al borrar vínculo(s) de empleado-dispositivo.");
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <summary>hiddenCount ahora puede venir tanto de "Mostrar dados de baja" como de
    /// cualquiera de los tres filtros nuevos (búsqueda, sucursal, estatus) — el mensaje ya
    /// no distingue la causa específica, solo deja claro que el total real es mayor al que
    /// se ve, para no dar a entender que esos empleados desaparecieron.</summary>
    private void RefreshStatusMessage(int hiddenCount)
    {
        if (_allRows.Count == 0)
        {
            StatusMessage = "Aún no hay empleados registrados.";
            return;
        }

        StatusMessage = hiddenCount > 0
            ? $"{Employees.Count} de {_allRows.Count} empleado(s) — {hiddenCount} oculto(s) por los filtros aplicados."
            : $"{Employees.Count} empleado(s) registrado(s) en la base local.";
    }

    /// <summary>Arma un CSV con los empleados actualmente visibles (respeta los filtros de
    /// Buscar/Sucursal/Estatus ya aplicados en pantalla) en el mismo formato CANÓNICO que
    /// espera "Reemplazar catálogo" (mismo encabezado — ver EmployeeCatalogReplaceParser) —
    /// pedido explícito del usuario: "necesito tener un botón para poder exportar catálogo
    /// de empleados". A diferencia de "Exportar plantilla" (encabezado + una fila de
    /// ejemplo, pensada para empezar de cero), esto exporta los datos REALES ya capturados:
    /// sirve como respaldo, o como punto de partida para editar en Excel y reimportar.
    ///
    /// Status colapsa a solo Activo/Inactivo (Active/OnLeave → Activo, Inactive/Terminated →
    /// Inactivo) porque es lo único que "Reemplazar catálogo" acepta en esa columna — un
    /// "De permiso" o "Baja" tal cual haría fallar el reimport; quien exporta y solo quiere
    /// un respaldo de lectura puede ignorar esa pérdida de matiz.</summary>
    public string BuildCatalogCsv()
    {
        var header = new[]
        {
            "Number", "FullName", "Area", "Position", "HireDate", "Status",
            "WeeklySalary", "OvertimeHourlyRate", "Notes", "Pin", "Department",
        };
        var lines = new List<string> { string.Join(",", header) };

        foreach (var row in Employees)
        {
            var employee = row.Employee;
            var status = employee.Status is EmploymentStatus.Inactive or EmploymentStatus.Terminated ? "Inactivo" : "Activo";
            var fields = new[]
            {
                employee.Number.Value,
                employee.FullName,
                row.BranchName,
                employee.Position ?? "",
                employee.HireDate.ToString("yyyy-MM-dd"),
                status,
                employee.WeeklySalary?.ToString("0.00") ?? "",
                employee.OvertimeHourlyRate?.ToString("0.00") ?? "",
                employee.Notes ?? "",
                row.PinSummary,
                employee.Department ?? "",
            };
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string CsvEscape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> DeleteEmployeeAsync(Guid employeeId)
    {
        try
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null)
            {
                return "No se encontró el empleado — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            // Baja lógica, no borrado físico: Employee conserva su historial (marcaciones,
            // vínculos a dispositivos) para consultas futuras — ver comentario de clase.
            employee.ChangeStatus(EmploymentStatus.Terminated);
            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al dar de baja un empleado (EmployeeId={EmployeeId})", employeeId);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    public sealed record HardDeleteEmployeesOutcome(
        int EmployeesDeleted, int MappingsDeleted, int AttendancesUnlinked, int PayrollDeductionsDeleted, string? Error)
    {
        public bool Success => Error is null;
    }

    /// <summary>Borrado FÍSICO y permanente de uno o más empleados — a diferencia de
    /// <see cref="DeleteEmployeeAsync"/> (baja lógica, la que usa "Eliminar" por fila), esto
    /// SÍ elimina el registro de verdad. Pedido explícito del usuario ("sí quiero borrado
    /// real y permanente ... para importar un nuevo documento y no haya conflicto"),
    /// entendiendo y aceptando que se pierde el historial de nómina de esos empleados.
    ///
    /// Orden de limpieza por cada empleado: primero sus EmployeeDeviceMapping (sin valor sin
    /// el empleado) y PayrollDeduction se BORRAN (a diferencia de Attendance, EmployeeId ahí
    /// es obligatorio — no se puede dejar "sin vincular"); luego sus Attendance se
    /// DESVINCULAN, nunca se borran (Attendance.ReconcileEmployee(null, null), igual que
    /// antes de que existiera el vínculo — es un registro de auditoría, ver comentario de
    /// clase de Attendance); al final se borra el propio Employee. Todo en un solo
    /// SaveChangesAsync, para que si algo falla a mitad de camino no quede nada a medias.
    ///
    /// NOTA de sincronización: SupabaseSyncBackgroundService solo hace upsert, nunca borra
    /// — un empleado eliminado aquí sigue existiendo en Supabase hasta limpiarlo aparte.
    /// </summary>
    public async Task<HardDeleteEmployeesOutcome> HardDeleteEmployeesAsync(IReadOnlyList<Guid> employeeIds)
    {
        try
        {
            var mappingsDeleted = 0;
            var attendancesUnlinked = 0;
            var deductionsDeleted = 0;
            var employeesDeleted = 0;

            foreach (var employeeId in employeeIds)
            {
                var employee = await _employeeRepository.GetByIdAsync(employeeId);
                if (employee is null)
                {
                    continue; // ya no existe (lista desactualizada) — se sigue con el resto
                }

                foreach (var mapping in await _mappingRepository.ListByEmployeeAsync(employeeId))
                {
                    await _mappingRepository.RemoveAsync(mapping);
                    mappingsDeleted++;
                }

                foreach (var deduction in await _payrollDeductionRepository.ListByEmployeeAsync(employeeId))
                {
                    await _payrollDeductionRepository.RemoveAsync(deduction);
                    deductionsDeleted++;
                }

                foreach (var attendance in await _attendanceRepository.ListByEmployeeAsync(employeeId))
                {
                    attendance.ReconcileEmployee(null, null);
                    attendancesUnlinked++;
                }

                await _employeeRepository.RemoveAsync(employee);
                employeesDeleted++;
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return new HardDeleteEmployeesOutcome(employeesDeleted, mappingsDeleted, attendancesUnlinked, deductionsDeleted, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al borrar físicamente empleados (Count={Count})", employeeIds.Count);
            return new HardDeleteEmployeesOutcome(
                0, 0, 0, 0, "Ocurrió un error inesperado al borrar. Revisa el registro de errores.");
        }
    }

    /// <summary>"🔍 Buscar repetidos": todos los registros (también los dados de baja) que parecen
    /// la misma persona — ver <see cref="EmployeeDuplicateFinder"/>.</summary>
    public async Task<IReadOnlyList<DuplicateGroup>> FindDuplicatesAsync()
    {
        var employees = await _employeeRepository.ListAsync();
        var employeesWithPin = (await _mappingRepository.ListAsync()).Select(m => m.EmployeeId).ToHashSet();
        var candidates = new List<DuplicateCandidate>(employees.Count);
        foreach (var employee in employees)
        {
            var punches = (await _attendanceRepository.ListByEmployeeAsync(employee.Id)).Count;
            candidates.Add(new DuplicateCandidate(employee, punches, employeesWithPin.Contains(employee.Id)));
        }

        return EmployeeDuplicateFinder.Find(candidates);
    }

    /// <summary>Qué hacer con un grupo de repetidos: conservar <paramref name="KeeperId"/> (con el
    /// nombre <paramref name="FullName"/>) y eliminar <paramref name="RemoveIds"/>.</summary>
    public sealed record DuplicateMergeRequest(Guid KeeperId, IReadOnlyList<Guid> RemoveIds, string FullName);

    public sealed record DuplicateMergeOutcome(
        int Deleted, int PunchesMoved, int PinsMoved, int DeductionsDeleted, bool CloudCleaned, string? Error)
    {
        public bool Success => Error is null;
    }

    /// <summary>Elimina los repetidos SIN perder nada: a quien se queda le pasan las marcaciones de
    /// los demás, sus PINs (en los relojes donde él no tenga uno) y los datos que le falten
    /// (horario, sueldo, puesto, teléfono, ...); después el repetido se BORRA de verdad (local y,
    /// si hay nube, también de Supabase). Sus deducciones de nómina se borran (no se pueden
    /// traspasar sin chocar con las de la misma semana). Todo en un solo guardado.</summary>
    public async Task<DuplicateMergeOutcome> MergeDuplicatesAsync(IReadOnlyList<DuplicateMergeRequest> requests)
    {
        var deletedIds = new List<Guid>();
        var removedMappingIds = new List<Guid>();
        var punchesMoved = 0;
        var pinsMoved = 0;
        var deductionsDeleted = 0;
        try
        {
            var allMappings = (await _mappingRepository.ListAsync()).ToList();
            foreach (var request in requests)
            {
                var keeper = await _employeeRepository.GetByIdAsync(request.KeeperId);
                if (keeper is null)
                {
                    continue;
                }

                var keeperDevices = allMappings.Where(m => m.EmployeeId == keeper.Id).Select(m => m.DeviceId).ToHashSet();
                foreach (var removeId in request.RemoveIds.Where(id => id != keeper.Id).Distinct())
                {
                    var duplicate = await _employeeRepository.GetByIdAsync(removeId);
                    if (duplicate is null)
                    {
                        continue;
                    }

                    MergeMissingData(keeper, duplicate);
                    if (keeper.Status == EmploymentStatus.Terminated && duplicate.Status != EmploymentStatus.Terminated)
                    {
                        keeper.ChangeStatus(duplicate.Status); // se queda el registro, pero vigente
                    }

                    foreach (var attendance in await _attendanceRepository.ListByEmployeeAsync(duplicate.Id))
                    {
                        attendance.ReconcileEmployee(keeper.Id, keeper.BranchId);
                        punchesMoved++;
                    }

                    foreach (var mapping in allMappings.Where(m => m.EmployeeId == duplicate.Id).ToList())
                    {
                        var tracked = await _mappingRepository.GetByIdAsync(mapping.Id);
                        if (tracked is null)
                        {
                            continue;
                        }

                        if (keeperDevices.Add(tracked.DeviceId))
                        {
                            tracked.ReassignEmployee(keeper.Id); // su PIN pasa a quien se queda
                            pinsMoved++;
                        }
                        else
                        {
                            await _mappingRepository.RemoveAsync(tracked); // quien se queda ya tiene PIN en ese reloj
                            removedMappingIds.Add(tracked.Id);
                        }
                        allMappings.Remove(mapping);
                    }

                    foreach (var deduction in await _payrollDeductionRepository.ListByEmployeeAsync(duplicate.Id))
                    {
                        await _payrollDeductionRepository.RemoveAsync(deduction);
                        deductionsDeleted++;
                    }

                    await _employeeRepository.RemoveAsync(duplicate);
                    deletedIds.Add(duplicate.Id);
                }

                var fullName = string.Join(' ', (request.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));
                if (fullName.Length > 0 && fullName != keeper.FullName)
                {
                    keeper.UpdatePersonalInfo(fullName, keeper.Department, keeper.Position);
                }
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _unitOfWork.DiscardPendingChanges();
            Log.Error(ex, "Error al eliminar empleados repetidos.");
            return new DuplicateMergeOutcome(0, 0, 0, 0, false,
                ex is DomainException ? ex.Message : "Ocurrió un error inesperado al guardar. No se cambió nada. Revisa el registro de errores.");
        }

        // Nube: primero se suben las marcaciones y PINs ya traspasados, luego se borran allá los
        // repetidos (si no, sus checadas en Supabase impedirían borrarlos).
        var cloudCleaned = true;
        if (_syncService.IsCloudConfigured && deletedIds.Count > 0)
        {
            if (removedMappingIds.Count > 0)
            {
                await _syncService.TryDeleteMappingsRemoteAsync(removedMappingIds);
            }
            cloudCleaned = await _syncService.TriggerSyncNowAsync()
                && await _syncService.TryDeleteEmployeesRemoteAsync(deletedIds);
        }

        await ReloadAsync();
        return new DuplicateMergeOutcome(deletedIds.Count, punchesMoved, pinsMoved, deductionsDeleted, cloudCleaned, null);
    }

    /// <summary>Una fila ya parseada (EmployeeImportRow) cruzada contra la base local
    /// real: si su sucursal existe o hay que crearla, si su número ya está en uso. Forma
    /// de UI — <see cref="RelojChecador.Application.Employees.EmployeeImportRow"/> es
    /// puramente del parseo del archivo, sin saber nada de la base de datos.</summary>
    public sealed record EmployeeImportPreviewRow(EmployeeImportRow Row, bool BranchExists, bool IsDuplicate)
    {
        /// <summary>Nunca se sobreescribe un empleado existente durante una importación
        /// masiva (regla explícita del usuario) — un número duplicado simplemente se
        /// omite, nunca actualiza al que ya estaba.</summary>
        public bool WillImport => !IsDuplicate;

        public IReadOnlyList<string> AllAlerts =>
        [
            .. Row.Alerts,
            .. BranchExists ? [] : (string[]) [$"Se creará la sucursal \"{Row.Area}\"."],
            .. IsDuplicate ? (string[]) ["Ya existe un empleado con este número — se omite."] : [],
        ];

        public string AlertsText => string.Join(" · ", AllAlerts);
    }

    public sealed record EmployeeImportPreview(
        IReadOnlyList<EmployeeImportPreviewRow> Rows, IReadOnlyList<string> ParseErrors, IReadOnlyList<string> BranchesToCreate)
    {
        public int TotalRows => Rows.Count;
        public int ToImport => Rows.Count(r => r.WillImport);
        public int Duplicates => Rows.Count(r => r.IsDuplicate);
        public int WithAlerts => Rows.Count(r => r.AllAlerts.Count > 0);
        public int WeeklySalaryPending => Rows.Count(r => r.Row.WeeklySalary is null);
    }

    /// <summary>Parsea el CSV y lo cruza contra la base local real (sucursales/números ya
    /// existentes) para armar la vista previa completa — no toca la base de datos, solo
    /// la consulta. Ver <see cref="ImportEmployeesAsync"/> para la ejecución real.</summary>
    public async Task<EmployeeImportPreview> PrepareImportPreviewAsync(IReadOnlyList<string> csvLines)
    {
        var parseResult = EmployeeImportParser.Parse(csvLines);

        var existingBranchNames = (await _branchRepository.ListAsync())
            .Select(b => b.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNumbers = _allRows
            .Select(r => r.Employee.Number.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenNumbersInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var previewRows = new List<EmployeeImportPreviewRow>();
        var branchesToCreate = new List<string>();

        foreach (var row in parseResult.Rows)
        {
            // Duplicado contra la base O contra otra fila anterior del MISMO archivo —
            // dos empleados con el mismo número en un solo CSV nunca deberían coexistir.
            var isDuplicate = existingNumbers.Contains(row.Number) || !seenNumbersInFile.Add(row.Number);
            var branchExists = existingBranchNames.Contains(row.Area);
            if (!branchExists && !branchesToCreate.Contains(row.Area, StringComparer.OrdinalIgnoreCase))
            {
                branchesToCreate.Add(row.Area);
            }

            previewRows.Add(new EmployeeImportPreviewRow(row, branchExists, isDuplicate));
        }

        return new EmployeeImportPreview(previewRows, parseResult.Errors, branchesToCreate);
    }

    /// <summary>Resultado de <see cref="ImportEmployeesAsync"/> — <see cref="Error"/> nulo
    /// significa éxito (async no admite parámetros <c>out</c>, de ahí el record en vez de
    /// una tupla con salida doble).</summary>
    public sealed record EmployeeImportOutcome(int Created, IReadOnlyList<string> BranchesCreated, string? Error)
    {
        public bool Success => Error is null;
    }

    /// <summary>Ejecuta la importación real: crea primero las sucursales que hagan falta
    /// (mismo <see cref="Branch.Create"/> que usa "+ Nueva sucursal"), luego los
    /// empleados nuevos (nunca los duplicados, ver <see cref="EmployeeImportPreviewRow.WillImport"/>)
    /// — todo en un solo <see cref="IUnitOfWork.SaveChangesAsync"/>, así que si algo falla
    /// a mitad de camino no queda nada a medias.</summary>
    /// <summary>Diccionario nombre→Id de sucursal, a salvo del ArgumentException críptico
    /// ("An item with the same key has already been added") que lanza un .ToDictionary liso
    /// si hay dos sucursales con el mismo nombre — nada en la base lo impide (Branch.Code es
    /// único, Branch.Name no; caso real). En vez de adivinar cuál de las dos usar (podría
    /// vincular gente a la sucursal equivocada sin que nadie se dé cuenta), se explica el
    /// problema con una DomainException — tanto ImportEmployeesAsync como
    /// ApplyCatalogReplaceAsync ya la manejan igual que cualquier otro error de
    /// validación.</summary>
    private static Dictionary<string, Guid> BuildBranchIdsByName(IReadOnlyList<Branch> branches)
    {
        var duplicateNames = branches
            .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicateNames.Count > 0)
        {
            throw new DomainException(
                $"Hay más de una sucursal con el mismo nombre en tu base ({string.Join(", ", duplicateNames)}) " +
                "— corrígelo en Sucursales (renombra o da de baja la que sobra) antes de continuar.");
        }

        return branches.ToDictionary(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<EmployeeImportOutcome> ImportEmployeesAsync(EmployeeImportPreview preview)
    {
        try
        {
            var branchIdsByName = BuildBranchIdsByName(await _branchRepository.ListAsync());

            var branchesCreated = new List<string>();
            foreach (var areaName in preview.BranchesToCreate)
            {
                if (branchIdsByName.ContainsKey(areaName))
                {
                    continue; // ya se creó como parte de esta misma corrida (dos filas con la misma área nueva)
                }

                // Código derivado del nombre (sin espacios, mayúsculas) — solo necesita
                // ser único, no tiene un significado de negocio propio. Mismo huso horario
                // que la sucursal ya existente (un solo negocio en Mexicali).
                var code = new string(areaName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                var branch = Branch.Create(code, areaName, "America/Tijuana", legalEntityName: null, address: null);
                await _branchRepository.AddAsync(branch);
                branchIdsByName[areaName] = branch.Id;
                branchesCreated.Add(areaName);
            }

            var createdCount = 0;
            var today = DateOnly.FromDateTime(DateTime.Now);
            foreach (var previewRow in preview.Rows.Where(r => r.WillImport))
            {
                var row = previewRow.Row;
                var employee = Employee.Create(
                    EmployeeNumber.Create(row.Number), row.FullName, branchIdsByName[row.Area], today,
                    row.WeeklySalary, department: null, position: row.Position, row.OvertimeHourlyRate);
                if (!string.IsNullOrWhiteSpace(row.Notes))
                {
                    employee.UpdateNotes(row.Notes);
                }
                await _employeeRepository.AddAsync(employee);
                createdCount++;
            }

            await _unitOfWork.SaveChangesAsync();
            await ReloadAsync();
            return new EmployeeImportOutcome(createdCount, branchesCreated, Error: null);
        }
        catch (DomainException ex)
        {
            return new EmployeeImportOutcome(0, [], ex.Message);
        }
        catch (DbUpdateException ex)
        {
            Log.Warning(ex, "No se pudo completar la importación masiva de empleados.");
            return new EmployeeImportOutcome(0, [],
                "No se pudo guardar la importación — revisa que no haya números de empleado duplicados dentro del mismo archivo.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado durante la importación masiva de empleados.");
            return new EmployeeImportOutcome(0, [], "Ocurrió un error inesperado al importar. Revisa el registro de errores.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // "Reemplazar catálogo maestro" — a diferencia de "Importar desde CSV" (arriba, que
    // solo agrega gente NUEVA y nunca toca a quien ya existe, regla explícita del usuario),
    // este flujo trata al archivo como la fuente de verdad completa: actualiza a quien
    // coincide por nombre, crea a quien es nuevo, y da de baja (lógica, nunca borra) a
    // quien ya no aparece. Pedido explícito del usuario tras subir un catálogo más completo
    // (Excel con fecha de ingreso real, estatus, etc.): "el excel que te pasé es el único
    // registro que quiero actualmente". Ver EmployeeCatalogReplaceParser para el formato.
    // ─────────────────────────────────────────────────────────────────────────

    /// <param name="Absorbed">Registros viejos dados de baja de esta misma persona que se unen a
    /// ella (ver EmployeeCatalogPinPlanner).</param>
    /// <param name="SkipReason">No null = la fila no se aplica (es la misma persona que otra fila).</param>
    /// <param name="DeviceId">Reloj donde se vincula el PIN de la fila, si se pudo resolver.</param>
    public sealed record EmployeeCatalogPreviewRow(
        EmployeeCatalogRow Row, Employee? ExistingMatch, bool BranchExists,
        IReadOnlyList<Employee>? Absorbed = null, string? SkipReason = null, Guid? DeviceId = null)
    {
        public IReadOnlyList<Employee> AbsorbedEmployees => Absorbed ?? [];
        public bool Skip => SkipReason is not null;
        public bool WillCreate => ExistingMatch is null && !Skip;
        public bool WillReactivate => ExistingMatch is { Status: EmploymentStatus.Terminated } && !Skip;

        public string Action => Skip ? "Omitir" : WillCreate ? "Crear" : WillReactivate ? "Reactivar" : "Actualizar";

        /// <summary>A quién del sistema corresponde la fila — para revisar la vista previa.</summary>
        public string MatchText => ExistingMatch is { } e ? $"{e.Number.Value} · {e.FullName}" : "";

        public IReadOnlyList<string> AllAlerts =>
        [
            .. SkipReason is null ? [] : (string[]) [SkipReason],
            .. Row.Alerts,
            .. AbsorbedEmployees.Select(a =>
                $"Se une con {a.Number.Value} · {a.FullName} (baja): sus marcaciones y los datos que falten pasan aquí."),
            .. BranchExists ? [] : (string[]) [$"Se creará la sucursal \"{Row.Area}\"."],
        ];

        public string AlertsText => string.Join(" · ", AllAlerts);
    }

    /// <summary>Un empleado ya existente que NO aparece (por nombre) en el catálogo nuevo —
    /// se dará de baja (EmploymentStatus.Terminated) al aplicar, salvo que esté protegido
    /// (ver <see cref="PrepareCatalogReplacePreviewAsync"/>).</summary>
    public sealed record EmployeeCatalogRemovalRow(Employee Employee, string BranchName)
    {
        public string DisplayText => $"{Employee.Number.Value} — {Employee.FullName} ({BranchName})";
    }

    public sealed record EmployeeCatalogReplacePreview(
        IReadOnlyList<EmployeeCatalogPreviewRow> Rows,
        IReadOnlyList<EmployeeCatalogRemovalRow> ToRemove,
        IReadOnlyList<string> ParseErrors,
        IReadOnlyList<string> BranchesToCreate)
    {
        public int TotalRows => Rows.Count;
        public int ToCreate => Rows.Count(r => r.WillCreate);
        public int ToUpdate => Rows.Count(r => !r.WillCreate && !r.Skip);
        public int ToReactivate => Rows.Count(r => r.WillReactivate);
        public int ToSkip => Rows.Count(r => r.Skip);
        public int ToMerge => Rows.Sum(r => r.AbsorbedEmployees.Count);
    }

    /// <summary>Arma la vista previa completa: parsea el archivo, decide a qué empleado
    /// corresponde cada fila, y calcula quién NO aparece en el archivo y por lo tanto se daría
    /// de baja. No toca la base de datos, solo la consulta.
    ///
    /// Filas CON PIN (y un solo reloj habilitado en su sucursal): se resuelven por PIN y luego por
    /// nombre (EmployeeCatalogPinPlanner) — el reloj es la fuente de verdad y cada persona tenía
    /// dos registros (el vigente con el PIN y las checadas, y uno viejo dado de baja con el
    /// nombre completo); el viejo se une al vigente en vez de reactivarse aparte. Filas SIN PIN:
    /// por nombre EXACTO (normalizado: espacios colapsados, sin distinguir mayúsculas),
    /// prefiriendo al vigente — nunca adivina con coincidencia parcial, por eso existe la vista
    /// previa.
    ///
    /// <paramref name="protectedNames"/>: nombres que deben conservarse tal cual aunque no
    /// estén en el archivo — p. ej. alguien dado de alta directo en el reloj que nunca pasó
    /// por ningún catálogo.</summary>
    public async Task<EmployeeCatalogReplacePreview> PrepareCatalogReplacePreviewAsync(
        IReadOnlyList<string> csvLines, IReadOnlyList<string> protectedNames)
    {
        var employees = await _employeeRepository.ListAsync();
        csvLines = FillMissingCatalogNumbers(csvLines, employees);
        var parseResult = EmployeeCatalogReplaceParser.Parse(csvLines);

        var branches = await _branchRepository.ListAsync();
        var existingBranchNames = branches.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mappings = await _mappingRepository.ListAsync();
        var devices = await _deviceRepository.ListAsync();

        // 1) Filas con PIN, por reloj.
        var planByIndex = new Dictionary<int, (CatalogPinPlan Plan, Guid DeviceId)>();
        var pinRowsByDevice = parseResult.Rows
            .Select((row, index) => (Row: row, Index: index, Device: ResolveCatalogDevice(row.Area, branches, devices)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Row.Pin) && x.Device is not null)
            .GroupBy(x => x.Device!.Id);
        foreach (var group in pinRowsByDevice)
        {
            var ownerByPin = mappings.Where(m => m.DeviceId == group.Key).ToDictionary(m => m.DeviceUserPin, m => m.EmployeeId);
            var pinsWithPunches = await _attendanceRepository.ListPinsWithAttendancesAsync(group.Key);
            var plans = EmployeeCatalogPinPlanner.Plan(
                [.. group.Select(x => new CatalogPinRow(x.Index, x.Row.Number, x.Row.FullName, x.Row.Pin!))],
                employees, ownerByPin, pinsWithPunches);
            foreach (var plan in plans)
            {
                planByIndex[plan.Row.Index] = (plan, group.Key);
            }
        }

        var matchedEmployeeIds = planByIndex.Values
            .SelectMany(p => p.Plan.Absorbed.Select(a => a.Id).Append(p.Plan.Survivor?.Id ?? Guid.Empty))
            .Where(id => id != Guid.Empty)
            .ToHashSet();

        // 2) Filas sin PIN: por nombre exacto, el vigente primero.
        var existingByName = new Dictionary<string, Employee>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees.OrderBy(e => e.Status == EmploymentStatus.Terminated))
        {
            existingByName.TryAdd(NormalizeName(employee.FullName), employee);
        }

        var previewRows = new List<EmployeeCatalogPreviewRow>();
        var branchesToCreate = new List<string>();
        for (var index = 0; index < parseResult.Rows.Count; index++)
        {
            var row = parseResult.Rows[index];
            var branchExists = existingBranchNames.Contains(row.Area);
            if (!branchExists && !branchesToCreate.Contains(row.Area, StringComparer.OrdinalIgnoreCase))
            {
                branchesToCreate.Add(row.Area);
            }

            if (planByIndex.TryGetValue(index, out var planned))
            {
                var plan = planned.Plan;
                var skipReason = plan.Resolution == CatalogPinResolution.SkipDuplicate
                    ? $"Es la misma persona que el PIN {parseResult.Rows[plan.DuplicateOfIndex!.Value].Pin}: esta fila se omite " +
                      "(si es un PIN de relleno, bórralo desde Dispositivos → Usuarios del reloj)."
                    : null;
                previewRows.Add(new EmployeeCatalogPreviewRow(
                    row, plan.Survivor, branchExists, plan.Absorbed, skipReason, planned.DeviceId));
                continue;
            }

            existingByName.TryGetValue(NormalizeName(row.FullName), out var match);
            if (match is not null && !matchedEmployeeIds.Add(match.Id))
            {
                match = null; // ya lo tomó otra fila
            }

            // Sin coincidencia exacta: "Adrian Uribe Garcia" en el archivo ↔ "Adrian Uribe" en el
            // sistema (o al revés). Solo si hay UN único candidato vigente — si no, se crea y
            // después "Buscar repetidos" lo resuelve (v1.70.0: antes esto creaba un repetido).
            if (match is null && FindUniqueLookAlike(row.FullName, employees, matchedEmployeeIds) is { } lookAlike)
            {
                matchedEmployeeIds.Add(lookAlike.Id);
                match = lookAlike;
                row = row with { Alerts = [.. row.Alerts, $"Nombre parecido: se toma como {lookAlike.Number.Value} · {lookAlike.FullName}."] };
            }
            previewRows.Add(new EmployeeCatalogPreviewRow(row, match, branchExists));
        }

        var protectedSet = protectedNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(NormalizeName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toRemove = _allRows
            .Where(r => r.Employee.Status != EmploymentStatus.Terminated)
            .Where(r => !matchedEmployeeIds.Contains(r.Employee.Id))
            .Where(r => !protectedSet.Contains(NormalizeName(r.Employee.FullName)))
            .Select(r => new EmployeeCatalogRemovalRow(r.Employee, r.BranchName))
            .ToList();

        return new EmployeeCatalogReplacePreview(previewRows, toRemove, parseResult.Errors, branchesToCreate);
    }

    /// <summary>El reloj donde vincular el PIN de una fila: el ÚNICO reloj habilitado de su
    /// sucursal. Antes se contaban también los deshabilitados — CAR-WASH tiene "Checador" más 5
    /// relojes de prueba deshabilitados, así que ningún PIN de ningún catálogo se vinculaba.</summary>
    private static Device? ResolveCatalogDevice(string area, IReadOnlyList<Branch> branches, IReadOnlyList<Device> devices)
    {
        var branch = branches.FirstOrDefault(b => string.Equals(b.Name, area, StringComparison.OrdinalIgnoreCase));
        if (branch is null)
        {
            return null;
        }

        var enabled = devices.Where(d => d.BranchId == branch.Id && d.Status != DeviceStatus.Disabled).ToList();
        return enabled.Count == 1 ? enabled[0] : null;
    }

    /// <summary>Sin mayúsculas, acentos ni espacios de más ("José Peña" = "JOSE PENA") — antes solo
    /// colapsaba espacios, y un archivo guardado sin acentos no reconocía a nadie (v1.70.0).</summary>
    private static string NormalizeName(string name) => DeviceUserEmployeeMatcher.NormalizeName(name);

    /// <summary>El único empleado vigente (no dado de baja, no tomado ya) cuyo nombre "se parece"
    /// al del archivo: todas las palabras del más corto (mínimo dos) están, en orden, en el más
    /// largo. Si hay cero o más de uno, null — nunca se adivina entre varios.</summary>
    private static Employee? FindUniqueLookAlike(string fullName, IReadOnlyList<Employee> employees, ISet<Guid> taken)
    {
        var words = NormalizeName(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidates = employees
            .Where(e => e.Status != EmploymentStatus.Terminated && !taken.Contains(e.Id))
            .Where(e =>
            {
                var other = NormalizeName(e.FullName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return Math.Min(words.Length, other.Length) >= 2 && DeviceUserEmployeeMatcher.NamesLookAlike(fullName, e.FullName);
            })
            .Take(2)
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Un archivo reconocido automáticamente puede no traer número de empleado (ni PIN):
    /// el convertidor lo deja vacío y aquí se completa — el número que ya tiene quien existe con
    /// ese nombre (el vigente primero), o el siguiente "EMP-###" libre para alguien nuevo. Así
    /// reemplazar el catálogo nunca le cambia el número a nadie por no venir en el archivo.
    /// Solo aplica al catálogo canónico (Number en la 1.ª columna, FullName en la 2.ª).</summary>
    private static IReadOnlyList<string> FillMissingCatalogNumbers(IReadOnlyList<string> csvLines, IReadOnlyList<Employee> employees)
    {
        if (csvLines.Count < 2 || !csvLines[0].StartsWith("Number,FullName,", StringComparison.Ordinal))
        {
            return csvLines;
        }

        var numberByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var employee in employees.OrderBy(e => e.Status == EmploymentStatus.Terminated))
        {
            numberByName.TryAdd(NormalizeName(employee.FullName), employee.Number.Value);
        }

        var used = employees.Select(e => e.Number.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var line in csvLines.Skip(1))
        {
            var number = CsvLineParser.SplitLine(line, ',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(number))
            {
                used.Add(number);
            }
        }

        var next = used
            .Select(n => n.StartsWith("EMP-", StringComparison.OrdinalIgnoreCase) && int.TryParse(n[4..], out var value) ? value : 0)
            .DefaultIfEmpty(0).Max() + 1;

        var result = new List<string>(csvLines.Count) { csvLines[0] };
        foreach (var line in csvLines.Skip(1))
        {
            var fields = CsvLineParser.SplitLine(line, ',');
            if (fields.Length < 2 || fields[0].Trim().Length > 0 || fields[1].Trim().Length == 0)
            {
                result.Add(line);
                continue;
            }

            var number = numberByName.TryGetValue(NormalizeName(fields[1]), out var existingNumber)
                ? existingNumber
                : FindUniqueLookAlike(fields[1], employees, new HashSet<Guid>())?.Number.Value;
            if (number is null)
            {
                do
                {
                    number = $"EMP-{next++:000}";
                }
                while (!used.Add(number));
            }

            fields[0] = number;
            result.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        return result;
    }

    public sealed record EmployeeCatalogReplaceOutcome(
        int Created, int Updated, int Removed, int Linked, IReadOnlyList<string> BranchesCreated,
        IReadOnlyList<string> PinWarnings, string? Error, int Merged = 0, int PunchesRecovered = 0)
    {
        public bool Success => Error is null;
    }

    /// <summary>Ejecuta el reemplazo real: crea sucursales que hagan falta, luego
    /// actualiza/crea cada fila del catálogo (uniendo los registros viejos de la misma persona y
    /// vinculando su PIN al reloj — ver más abajo), y por último da de baja a quien no apareció.
    /// Actualizar NUNCA borra un dato ya capturado solo porque el archivo nuevo no lo trae
    /// (sueldo, puesto, departamento, horario…) — mismo criterio de "null nunca sobreescribe un
    /// dato real" que el resto del proyecto; así una lista corta de solo número y nombre no borra
    /// nada.
    ///
    /// Se guarda en DOS pasos: primero se liberan los números que el archivo le da a otra persona
    /// (el registro viejo "1 · Adrian Uribe Garcia" pasa a "1-baja" para que el vigente pueda
    /// tomar el "1") y se sube a la nube, y luego todo lo demás. En Supabase Employee.Number es
    /// único y cada tabla se sube en UN solo lote: con ambos cambios en el mismo lote, el
    /// resultado dependería del orden de las filas y podría fallar en cada ciclo para siempre.
    ///
    /// El PIN de la fila solo crea/corrige el vínculo LOCAL (EmployeeDeviceMapping) — nunca
    /// se conecta a ningún dispositivo físico aquí. Para que el PIN llegue de verdad al reloj,
    /// "Enviar empleados al reloj" sube a cualquiera que el reloj real todavía no tenga.</summary>
    public async Task<EmployeeCatalogReplaceOutcome> ApplyCatalogReplaceAsync(EmployeeCatalogReplacePreview preview)
    {
        var rows = preview.Rows.Where(r => !r.Skip).ToList();
        var renamed = new List<(Employee Employee, EmployeeNumber Original)>();
        try
        {
            // ── Paso 1: liberar los números que el archivo le asigna a otra persona. ──
            var allEmployees = await _employeeRepository.ListAsync();
            var intendedOwnerByNumber = new Dictionary<string, Guid?>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in rows)
            {
                intendedOwnerByNumber.TryAdd(row.Row.Number, row.ExistingMatch?.Id);
            }
            var survivorIds = rows.Where(r => r.ExistingMatch is not null).Select(r => r.ExistingMatch!.Id).ToHashSet();
            var goingAwayIds = rows.SelectMany(r => r.AbsorbedEmployees).Select(a => a.Id)
                .Concat(preview.ToRemove.Select(r => r.Employee.Id))
                .ToHashSet();
            var usedNumbers = allEmployees.Select(e => e.Number.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var employee in allEmployees)
            {
                if (!intendedOwnerByNumber.TryGetValue(employee.Number.Value, out var intendedOwner) || intendedOwner == employee.Id)
                {
                    continue;
                }

                var suffix = survivorIds.Contains(employee.Id) ? "-tmp"
                    : employee.Status == EmploymentStatus.Terminated || goingAwayIds.Contains(employee.Id) ? "-baja"
                    : "-anterior";
                var freed = UniqueEmployeeNumber(employee.Number.Value, suffix, usedNumbers);
                renamed.Add((employee, employee.Number));
                employee.ChangeNumber(EmployeeNumber.Create(freed));
            }

            if (renamed.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync();
                if (_syncService.IsCloudConfigured && !await _syncService.TriggerSyncNowAsync())
                {
                    await RevertRenamedNumbersAsync(renamed);
                    return new EmployeeCatalogReplaceOutcome(0, 0, 0, 0, [], [],
                        "No se pudo sincronizar con la nube (¿sin internet?) antes de cambiar los números de empleado. " +
                        "No se cambió nada — revisa la conexión y vuelve a aplicar.");
                }
            }

            // ── Paso 2: todo lo demás, en un solo guardado. ──
            var branchIdsByName = BuildBranchIdsByName(await _branchRepository.ListAsync());

            var branchesCreated = new List<string>();
            foreach (var areaName in preview.BranchesToCreate)
            {
                if (branchIdsByName.ContainsKey(areaName))
                {
                    continue; // ya se creó como parte de esta misma corrida
                }

                var code = new string(areaName.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
                var branch = Branch.Create(code, areaName, "America/Tijuana", legalEntityName: null, address: null);
                await _branchRepository.AddAsync(branch);
                branchIdsByName[areaName] = branch.Id;
                branchesCreated.Add(areaName);
            }

            // Solo relojes habilitados — ver ResolveCatalogDevice.
            var devicesByBranch = (await _deviceRepository.ListAsync())
                .Where(d => d.Status != DeviceStatus.Disabled)
                .GroupBy(d => d.BranchId)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Estado de vínculos, mutable a lo largo de todo el reemplazo — varias filas del mismo
            // archivo pueden interactuar entre sí, así que se actualiza en memoria en cada vuelta.
            var existingMappings = (await _mappingRepository.ListAsync()).ToList();
            var mappingByDeviceAndEmployee = existingMappings.ToDictionary(m => (m.DeviceId, m.EmployeeId), m => m);
            var mappingByDeviceAndPin = existingMappings.ToDictionary(m => (m.DeviceId, m.DeviceUserPin), m => m);
            var removedMappingIds = new List<Guid>();
            var employeesById = allEmployees.ToDictionary(e => e.Id);

            async Task RemoveMappingAsync(EmployeeDeviceMapping mapping)
            {
                mappingByDeviceAndEmployee.Remove((mapping.DeviceId, mapping.EmployeeId));
                mappingByDeviceAndPin.Remove((mapping.DeviceId, mapping.DeviceUserPin));
                await _mappingRepository.RemoveAsync(mapping);
                removedMappingIds.Add(mapping.Id);
            }

            var today = DateOnly.FromDateTime(DateTime.Now);
            var created = 0;
            var updated = 0;
            var linked = 0;
            var merged = 0;
            var punchesRecovered = 0;
            var pinWarnings = new List<string>();

            foreach (var previewRow in rows)
            {
                var row = previewRow.Row;
                var branchId = branchIdsByName[row.Area];
                Employee employee;

                if (previewRow.ExistingMatch is null)
                {
                    employee = Employee.Create(
                        EmployeeNumber.Create(row.Number), row.FullName, branchId,
                        row.HireDate ?? today, row.WeeklySalary, row.Department, row.Position, row.OvertimeHourlyRate,
                        hasSpecialSchedule: row.HasSpecialSchedule ?? false);
                    if (row.Status != EmploymentStatus.Active)
                    {
                        employee.ChangeStatus(row.Status);
                    }
                    if (!string.IsNullOrWhiteSpace(row.Notes))
                    {
                        employee.UpdateNotes(row.Notes);
                    }
                    if (row.ScheduledStartTime is not null && row.ScheduledEndTime is not null)
                    {
                        employee.UpdateSchedule(row.ScheduledStartTime, row.ScheduledEndTime);
                    }
                    await _employeeRepository.AddAsync(employee);
                    employeesById[employee.Id] = employee;
                    created++;
                }
                else
                {
                    employee = await _employeeRepository.GetByIdAsync(previewRow.ExistingMatch.Id)
                        ?? throw new InvalidOperationException(
                            $"No se encontró a \"{row.FullName}\" — la lista pudo haber cambiado. Cierra este diálogo y vuelve a intentar.");

                    // Puesto/Departamento vacíos en el archivo NO borran lo ya capturado.
                    employee.UpdatePersonalInfo(row.FullName, row.Department ?? employee.Department, row.Position ?? employee.Position);
                    if (row.HireDate is { } hireDate)
                    {
                        employee.UpdateHireDate(hireDate);
                    }
                    if (employee.BranchId != branchId)
                    {
                        employee.TransferToBranch(branchId);
                    }
                    if (!string.Equals(employee.Number.Value, row.Number, StringComparison.OrdinalIgnoreCase))
                    {
                        employee.ChangeNumber(EmployeeNumber.Create(row.Number));
                    }
                    if (row.WeeklySalary is not null || row.OvertimeHourlyRate is not null)
                    {
                        employee.UpdateCompensation(
                            row.WeeklySalary ?? employee.WeeklySalary,
                            row.OvertimeHourlyRate ?? employee.OvertimeHourlyRate);
                    }
                    employee.ChangeStatus(row.Status);
                    if (!string.IsNullOrWhiteSpace(row.Notes))
                    {
                        employee.UpdateNotes(row.Notes);
                    }
                    if (row.ScheduledStartTime is not null && row.ScheduledEndTime is not null)
                    {
                        employee.UpdateSchedule(row.ScheduledStartTime, row.ScheduledEndTime);
                    }
                    if (row.HasSpecialSchedule is { } hasSpecialSchedule)
                    {
                        employee.SetSpecialSchedule(hasSpecialSchedule);
                    }
                    updated++;
                }

                // Registros viejos de la misma persona: completan lo que le falte y le pasan sus
                // marcaciones; ellos se quedan dados de baja, con una nota de a quién se unieron.
                foreach (var absorbedPreview in previewRow.AbsorbedEmployees)
                {
                    var absorbed = await _employeeRepository.GetByIdAsync(absorbedPreview.Id);
                    if (absorbed is null)
                    {
                        continue;
                    }

                    MergeMissingData(employee, absorbed);
                    foreach (var attendance in await _attendanceRepository.ListByEmployeeAsync(absorbed.Id))
                    {
                        attendance.ReconcileEmployee(employee.Id, employee.BranchId);
                        punchesRecovered++;
                    }
                    if (absorbed.Status != EmploymentStatus.Terminated)
                    {
                        absorbed.ChangeStatus(EmploymentStatus.Terminated);
                    }
                    absorbed.UpdateNotes(string.Join(" | ", new[] { absorbed.Notes,
                        $"Unido a {employee.Number.Value} · {employee.FullName} al reemplazar el catálogo ({today:dd/MM/yyyy})." }
                        .Where(n => !string.IsNullOrWhiteSpace(n))));
                    merged++;
                }

                if (string.IsNullOrWhiteSpace(row.Pin))
                {
                    continue;
                }

                Guid deviceId;
                if (previewRow.DeviceId is { } plannedDeviceId)
                {
                    deviceId = plannedDeviceId;
                }
                else if (devicesByBranch.TryGetValue(branchId, out var branchDevices) && branchDevices.Count == 1)
                {
                    deviceId = branchDevices[0].Id;
                }
                else
                {
                    pinWarnings.Add(branchDevices is null or { Count: 0 }
                        ? $"\"{row.FullName}\": PIN {row.Pin} no se vinculó — su sucursal (\"{row.Area}\") no tiene ningún reloj habilitado."
                        : $"\"{row.FullName}\": PIN {row.Pin} no se vinculó — su sucursal (\"{row.Area}\") tiene varios relojes habilitados, vincúlalo a mano desde Empleados.");
                    continue;
                }

                // El PIN: si lo tiene alguien que se va (dado de baja, unido o que no está en el
                // archivo) se le pasa a esta persona; si lo tiene otra persona del archivo, no.
                if (mappingByDeviceAndPin.TryGetValue((deviceId, row.Pin), out var pinMapping) && pinMapping.EmployeeId != employee.Id)
                {
                    var ownerStays = survivorIds.Contains(pinMapping.EmployeeId) && !goingAwayIds.Contains(pinMapping.EmployeeId);
                    if (ownerStays)
                    {
                        pinWarnings.Add($"\"{row.FullName}\": PIN {row.Pin} ya lo usa otro empleado del archivo en ese reloj — no se vinculó, elige un PIN distinto.");
                        continue;
                    }

                    if (mappingByDeviceAndEmployee.TryGetValue((deviceId, employee.Id), out var ownOtherMapping))
                    {
                        await RemoveMappingAsync(ownOtherMapping); // su PIN "de relleno"
                    }

                    var tracked = await _mappingRepository.GetByIdAsync(pinMapping.Id)
                        ?? throw new InvalidOperationException($"No se encontró el vínculo del PIN {row.Pin}. Vuelve a intentar.");
                    mappingByDeviceAndEmployee.Remove((deviceId, tracked.EmployeeId));
                    tracked.ReassignEmployee(employee.Id);
                    mappingByDeviceAndEmployee[(deviceId, employee.Id)] = tracked;
                    linked++;
                }
                else if (pinMapping is null)
                {
                    if (mappingByDeviceAndEmployee.TryGetValue((deviceId, employee.Id), out var existingMapping))
                    {
                        var tracked = await _mappingRepository.GetByIdAsync(existingMapping.Id)
                            ?? throw new InvalidOperationException($"No se encontró el vínculo de \"{row.FullName}\". Vuelve a intentar.");
                        mappingByDeviceAndPin.Remove((deviceId, tracked.DeviceUserPin));
                        tracked.UpdatePin(row.Pin);
                        mappingByDeviceAndPin[(deviceId, row.Pin)] = tracked;
                    }
                    else
                    {
                        var newMapping = EmployeeDeviceMapping.Create(employee.Id, deviceId, row.Pin);
                        await _mappingRepository.AddAsync(newMapping);
                        mappingByDeviceAndEmployee[(deviceId, employee.Id)] = newMapping;
                        mappingByDeviceAndPin[(deviceId, row.Pin)] = newMapping;
                    }
                    linked++;
                }

                // Marcaciones de ese PIN que no son de nadie vigente (sin dueño, o de alguien dado
                // de baja) — las que "checaron en el reloj pero no aparecían en el sistema".
                foreach (var attendance in await _attendanceRepository.ListByDeviceAndPinAsync(deviceId, row.Pin))
                {
                    if (attendance.EmployeeId == employee.Id)
                    {
                        continue;
                    }

                    var ownerStays = attendance.EmployeeId is { } ownerId && survivorIds.Contains(ownerId) && !goingAwayIds.Contains(ownerId);
                    var ownerActive = attendance.EmployeeId is { } id && employeesById.TryGetValue(id, out var owner)
                        && owner.Status != EmploymentStatus.Terminated && !goingAwayIds.Contains(id);
                    if (ownerStays || ownerActive)
                    {
                        continue;
                    }

                    attendance.ReconcileEmployee(employee.Id, employee.BranchId);
                    punchesRecovered++;
                }
            }

            var removed = 0;
            foreach (var removal in preview.ToRemove)
            {
                var employee = await _employeeRepository.GetByIdAsync(removal.Employee.Id);
                if (employee is null)
                {
                    continue;
                }
                employee.ChangeStatus(EmploymentStatus.Terminated);
                removed++;
            }

            await _unitOfWork.SaveChangesAsync();

            // Los vínculos quitados se borran también de la nube (la sincronización solo sube, nunca
            // borra); si falla, allá queda una fila huérfana que no afecta a la app.
            if (removedMappingIds.Count > 0 && _syncService.IsCloudConfigured &&
                !await _syncService.TryDeleteMappingsRemoteAsync(removedMappingIds))
            {
                Log.Warning("No se pudieron borrar en Supabase {Count} vínculo(s) quitados al reemplazar el catálogo.", removedMappingIds.Count);
            }
            _ = _syncService.TriggerSyncNowAsync();

            await ReloadAsync();
            return new EmployeeCatalogReplaceOutcome(
                created, updated, removed, linked, branchesCreated, pinWarnings, Error: null, merged, punchesRecovered);
        }
        catch (Exception ex)
        {
            _unitOfWork.DiscardPendingChanges();
            await RevertRenamedNumbersAsync(renamed);

            return ex switch
            {
                DomainException domainException => new EmployeeCatalogReplaceOutcome(0, 0, 0, 0, [], [], domainException.Message),
                DbUpdateException => LogAndFail(ex, Serilog.Events.LogEventLevel.Warning,
                    "No se pudo guardar — revisa que no haya números de empleado duplicados dentro del archivo ni contra alguien que ya existe."),
                _ => LogAndFail(ex, Serilog.Events.LogEventLevel.Error, "Ocurrió un error inesperado al guardar. Revisa el registro de errores."),
            };
        }

        static EmployeeCatalogReplaceOutcome LogAndFail(Exception ex, Serilog.Events.LogEventLevel level, string message)
        {
            Log.Write(level, ex, "No se pudo aplicar el reemplazo de catálogo de empleados.");
            return new EmployeeCatalogReplaceOutcome(0, 0, 0, 0, [], [], message);
        }
    }

    /// <summary>Deshace el paso 1 de <see cref="ApplyCatalogReplaceAsync"/> si el paso 2 no se pudo
    /// completar — cada quien recupera su número original.</summary>
    private async Task RevertRenamedNumbersAsync(IReadOnlyList<(Employee Employee, EmployeeNumber Original)> renamed)
    {
        if (renamed.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var (employee, original) in renamed)
            {
                employee.ChangeNumber(original);
            }
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _unitOfWork.DiscardPendingChanges();
            Log.Error(ex, "No se pudieron restaurar los números de empleado tras un reemplazo de catálogo fallido.");
        }
    }

    /// <summary>"1" + "-baja" → "1-baja" (o "1-baja2", … si ya existe), dentro del máximo de 20
    /// caracteres de EmployeeNumber.</summary>
    private static string UniqueEmployeeNumber(string number, string suffix, HashSet<string> usedNumbers)
    {
        for (var attempt = 1; ; attempt++)
        {
            var tail = attempt == 1 ? suffix : $"{suffix}{attempt}";
            var candidate = (number.Length + tail.Length > 20 ? number[..(20 - tail.Length)] : number) + tail;
            if (usedNumbers.Add(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>Completa en <paramref name="target"/> solo lo que no tiene, con lo del registro
    /// viejo de la misma persona — nunca pisa un dato ya capturado.</summary>
    private static void MergeMissingData(Employee target, Employee source)
    {
        if (target.ScheduledStartTime is null && source.ScheduledStartTime is not null && source.ScheduledEndTime is not null)
        {
            target.UpdateSchedule(source.ScheduledStartTime, source.ScheduledEndTime);
        }
        if (source.HasSpecialSchedule && !target.HasSpecialSchedule)
        {
            target.SetSpecialSchedule(true);
        }
        if ((target.WeeklySalary is null && source.WeeklySalary is not null) ||
            (target.OvertimeHourlyRate is null && source.OvertimeHourlyRate is not null))
        {
            target.UpdateCompensation(target.WeeklySalary ?? source.WeeklySalary, target.OvertimeHourlyRate ?? source.OvertimeHourlyRate);
        }
        if (target.Department is null && source.Department is not null || target.Position is null && source.Position is not null)
        {
            target.UpdatePersonalInfo(target.FullName, target.Department ?? source.Department, target.Position ?? source.Position);
        }
        if (target.Phone is null && source.Phone is not null || target.Email is null && source.Email is not null)
        {
            target.UpdateContact(target.Phone ?? source.Phone, target.Email ?? source.Email);
        }
        if (target.Rfc is null && source.Rfc is not null || target.Curp is null && source.Curp is not null || target.Nss is null && source.Nss is not null)
        {
            target.UpdateFiscalInfo(target.Rfc ?? source.Rfc, target.Curp ?? source.Curp, target.Nss ?? source.Nss);
        }
        if (string.IsNullOrWhiteSpace(target.Notes) && !string.IsNullOrWhiteSpace(source.Notes))
        {
            target.UpdateNotes(source.Notes);
        }
    }
}
