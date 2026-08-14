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
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.EmployeeDeviceMappings;
using RelojChecador.Domain.Employees;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// Une un <see cref="Employee"/> con el nombre de su sucursal y un resumen de sus
/// vínculos a dispositivos, ambos ya resueltos — Employee solo guarda BranchId (Guid) sin
/// navegación a Branch, y sus vínculos viven en una tabla aparte
/// (EmployeeDeviceMapping), así que el DataGrid bindea a esto en vez de al Employee crudo.
/// </summary>
public sealed record EmployeeRow(Employee Employee, string BranchName, string LinkedDevicesSummary);

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
    private readonly IUnitOfWork _unitOfWork;

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
        IUnitOfWork unitOfWork)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _mappingRepository = mappingRepository;
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
    }

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
        var linkedDevicesSummary = BuildLinkedDevicesSummary(employee.Id, deviceNamesById, mappings);
        return new EmployeeRow(employee, branchName, linkedDevicesSummary);
    }

    private static string BuildLinkedDevicesSummary(
        Guid employeeId, Dictionary<Guid, string> deviceNamesById, IReadOnlyList<EmployeeDeviceMapping> mappings)
    {
        var own = mappings.Where(m => m.EmployeeId == employeeId).ToList();
        if (own.Count == 0)
        {
            return "Sin vincular";
        }

        var parts = own.Select(m =>
        {
            var deviceName = deviceNamesById.TryGetValue(m.DeviceId, out var name) ? name : "(dispositivo desconocido)";
            return $"{deviceName} (PIN {m.DeviceUserPin})";
        });
        return string.Join(", ", parts);
    }

    /// <summary>Concilia con <paramref name="employeeId"/> las marcaciones de
    /// <paramref name="deviceId"/>+<paramref name="deviceUserPin"/> que llegaron ANTES de
    /// que existiera este vínculo (EmployeeId todavía null) — usa
    /// Attendance.ReconcileEmployee, que existía en el dominio sin que nada lo invocara
    /// hasta ahora. Se llama justo antes de SaveChangesAsync en los tres lugares donde se
    /// crea un EmployeeDeviceMapping, para que quede en la misma transacción que el
    /// vínculo — si algo falla al guardar, ninguna de las dos cosas queda a medias.</summary>
    private async Task ReconcileAttendancesAsync(Guid employeeId, Guid deviceId, string deviceUserPin)
    {
        var unresolved = await _attendanceRepository.ListUnresolvedByDeviceAndPinAsync(deviceId, deviceUserPin);
        foreach (var attendance in unresolved)
        {
            attendance.ReconcileEmployee(employeeId);
        }
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

    public async Task<IReadOnlyList<Device>> GetDevicesAsync() => await _deviceRepository.ListAsync();

    /// <param name="weeklySalary">Insumo de nómina sin cálculo fiscal — ver comentario de
    /// clase de Employee.</param>
    /// <param name="deviceId">Dispositivo a vincular en la misma operación, o null para
    /// no vincular ahora (se puede hacer después con "Vincular a dispositivo").</param>
    /// <param name="deviceUserPin">Requerido si <paramref name="deviceId"/> no es null.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateEmployeeAsync(
        string number, string fullName, Guid branchId, DateOnly hireDate, decimal? weeklySalary, string? department, string? position,
        decimal? overtimeHourlyRate = null, Guid? deviceId = null, string? deviceUserPin = null, string? notes = null)
    {
        try
        {
            var employee = Employee.Create(
                EmployeeNumber.Create(number), fullName, branchId, hireDate, weeklySalary, department, position, overtimeHourlyRate);
            if (!string.IsNullOrWhiteSpace(notes))
            {
                employee.UpdateNotes(notes);
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
        Guid? deviceId = null, string? deviceUserPin = null, string? notes = null)
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
    public async Task<EmployeeImportOutcome> ImportEmployeesAsync(EmployeeImportPreview preview)
    {
        try
        {
            var branchIdsByName = (await _branchRepository.ListAsync())
                .ToDictionary(b => b.Name, b => b.Id, StringComparer.OrdinalIgnoreCase);

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
}
