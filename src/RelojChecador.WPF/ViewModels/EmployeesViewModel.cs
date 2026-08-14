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

/// <summary>
/// ViewModel de la pantalla de Empleados: alta, edición y listado (Fase 3), más el vínculo
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

    [ObservableProperty]
    private string _statusMessage = "Cargando empleados...";

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

        Employees.Clear();
        foreach (var employee in employees)
        {
            Employees.Add(BuildRow(employee, branchNamesById, deviceNamesById, mappings));
        }

        RefreshStatusMessage();
    }

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

    /// <param name="deviceId">Dispositivo a vincular en la misma operación, o null para
    /// no vincular ahora (se puede hacer después con "Vincular a dispositivo").</param>
    /// <param name="deviceUserPin">Requerido si <paramref name="deviceId"/> no es null.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateEmployeeAsync(
        string number, string fullName, Guid branchId, DateOnly hireDate, string? department, string? position,
        Guid? deviceId = null, string? deviceUserPin = null)
    {
        try
        {
            var employee = Employee.Create(EmployeeNumber.Create(number), fullName, branchId, hireDate, department, position);
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
    /// <param name="deviceId">Dispositivo a vincular en la misma operación si el empleado
    /// todavía no tenía ninguno (ver EditEmployeeDialog) — null si no aplica.</param>
    /// <param name="deviceUserPin">Requerido si <paramref name="deviceId"/> no es null.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateEmployeeAsync(
        Guid employeeId, string number, string fullName, Guid branchId, string? department, string? position,
        string? phone, string? email, EmploymentStatus status, Guid? deviceId = null, string? deviceUserPin = null)
    {
        try
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null)
            {
                // No debería pasar en uso normal (la fila viene de una lista ya cargada de
                // la misma base), pero cubre la carrera de que alguien más lo haya borrado
                // — hoy imposible desde la UI (no hay eliminar empleados), pero defensivo.
                return "No se encontró el empleado — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            if (employee.Number.Value != number)
            {
                employee.ChangeNumber(EmployeeNumber.Create(number));
            }

            employee.UpdatePersonalInfo(fullName, department, position);
            employee.UpdateContact(phone, email);
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

    private void RefreshStatusMessage()
    {
        StatusMessage = Employees.Count == 0
            ? "Aún no hay empleados registrados."
            : $"{Employees.Count} empleado(s) registrado(s) en la base local.";
    }
}
