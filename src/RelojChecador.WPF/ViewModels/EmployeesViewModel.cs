using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// Une un <see cref="Employee"/> con el nombre de su sucursal ya resuelto —
/// Employee solo guarda BranchId (Guid), sin navegación a Branch (ver comentario de
/// EmployeesViewModel.InitializeAsync), así que el DataGrid bindea a esto en vez de al
/// Employee crudo.
/// </summary>
public sealed record EmployeeRow(Employee Employee, string BranchName);

/// <summary>
/// ViewModel de la pantalla de Empleados: alta y listado, primer tramo de la Fase 3
/// (navegación completa de la UI). Sigue el mismo patrón que MainViewModel (Sucursales):
/// solo lectura + alta por ahora, sin editar/eliminar.
/// </summary>
public sealed partial class EmployeesViewModel : ObservableObject
{
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;

    [ObservableProperty]
    private string _statusMessage = "Cargando empleados...";

    public ObservableCollection<EmployeeRow> Employees { get; } = [];

    public EmployeesViewModel(
        IEmployeeRepository employeeRepository, IBranchRepository branchRepository, IUnitOfWork unitOfWork)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var employees = await _employeeRepository.ListAsync();
            // Ambas listas se cargan completas (sin paginación, igual que Sucursales/
            // Dispositivos) y se cruzan en memoria: Employee no tiene navegación a Branch
            // en el dominio, así que el nombre de sucursal se resuelve aquí, no en la vista.
            var branches = await _branchRepository.ListAsync();
            var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);

            Employees.Clear();
            foreach (var employee in employees)
            {
                var branchName = branchNamesById.TryGetValue(employee.BranchId, out var name)
                    ? name
                    : "(sucursal desconocida)";
                Employees.Add(new EmployeeRow(employee, branchName));
            }

            RefreshStatusMessage();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de empleados.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

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
            Employees.Add(new EmployeeRow(employee, branchName));
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

    private void RefreshStatusMessage()
    {
        StatusMessage = Employees.Count == 0
            ? "Aún no hay empleados registrados."
            : $"{Employees.Count} empleado(s) registrado(s) en la base local.";
    }
}
