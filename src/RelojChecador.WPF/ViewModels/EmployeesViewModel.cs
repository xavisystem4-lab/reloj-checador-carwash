using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
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
/// ViewModel de la pantalla de Empleados: alta y listado (Fase 3), más el vínculo
/// Empleado↔Dispositivo (EmployeeDeviceMapping) — asocia el PIN interno que cada reloj usa
/// para reconocer a un empleado, prerequisito para que una futura pantalla de Asistencia
/// pueda mostrar nombres en vez de PINs crudos. El PIN se captura a mano en el diálogo
/// (LinkEmployeeDeviceDialog), no se descarga del dispositivo conectado — decisión de
/// alcance explícita para esta entrega.
/// </summary>
public sealed partial class EmployeesViewModel : ObservableObject
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;
    private readonly IUnitOfWork _unitOfWork;

    [ObservableProperty]
    private string _statusMessage = "Cargando empleados...";

    public ObservableCollection<EmployeeRow> Employees { get; } = [];

    public EmployeesViewModel(
        IEmployeeRepository employeeRepository, IBranchRepository branchRepository, IDeviceRepository deviceRepository,
        IEmployeeDeviceMappingRepository mappingRepository, IUnitOfWork unitOfWork)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _mappingRepository = mappingRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task InitializeAsync()
    {
        try
        {
            // Las cuatro listas se cargan completas (sin paginación, igual que
            // Sucursales/Dispositivos) y se cruzan en memoria: ni Employee tiene
            // navegación a Branch, ni hay una vía directa de Employee a sus dispositivos
            // vinculados — ambas se resuelven aquí, no en la vista.
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
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de empleados.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
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

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

    public async Task<IReadOnlyList<Device>> GetDevicesAsync() => await _deviceRepository.ListAsync();

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateEmployeeAsync(
        string number, string fullName, Guid branchId, DateOnly hireDate, string? department, string? position)
    {
        try
        {
            var employee = Employee.Create(EmployeeNumber.Create(number), fullName, branchId, hireDate, department, position);
            await _employeeRepository.AddAsync(employee);
            await _unitOfWork.SaveChangesAsync();

            var branches = await _branchRepository.ListAsync();
            var branchName = branches.FirstOrDefault(b => b.Id == branchId)?.Name ?? "(sucursal desconocida)";
            Employees.Add(new EmployeeRow(employee, branchName, "Sin vincular"));
            RefreshStatusMessage();
            return null;
        }
        catch (DomainException ex)
        {
            // Ej.: número/nombre vacíos — validación de negocio, mensaje ya comprensible.
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // El índice único de Employee.Number (ver EfEmployeeRepository/EmployeeConfiguration)
            // es lo que normalmente dispara esto — se interpreta como duplicado sin inspeccionar
            // el texto del error nativo de SQLite, que no es estable entre versiones.
            Log.Warning(ex, "No se pudo guardar el empleado, probablemente por número duplicado (Number={Number})", number);
            return "Ya existe un empleado con ese número.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear un empleado (Number={Number})", number);
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
            await _unitOfWork.SaveChangesAsync();

            // Se reemplaza la fila entera (EmployeeRow es un record inmutable) para que el
            // DataGrid refresque la columna "Dispositivos vinculados" de ese empleado vía
            // INotifyCollectionChanged — mutar un campo no dispararía el binding.
            var index = Employees.ToList().FindIndex(row => row.Employee.Id == employeeId);
            if (index >= 0)
            {
                var devices = await _deviceRepository.ListAsync();
                var deviceNamesById = devices.ToDictionary(d => d.Id, d => d.Name);
                var mappings = await _mappingRepository.ListAsync();
                var current = Employees[index];
                Employees[index] = current with { LinkedDevicesSummary = BuildLinkedDevicesSummary(employeeId, deviceNamesById, mappings) };
            }

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
