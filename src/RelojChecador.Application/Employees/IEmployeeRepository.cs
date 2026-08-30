using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Employee?> GetByNumberAsync(EmployeeNumber number, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Employee>> ListByBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);

    /// <summary>Borrado FÍSICO, no baja lógica (ver Employee.ChangeStatus para eso) —
    /// usado solo por el flujo explícito de "Borrar" en Empleados, que primero limpia sus
    /// EmployeeDeviceMapping/PayrollDeduction y desvincula sus Attendance (ver
    /// EmployeesViewModel.HardDeleteEmployeesAsync). Nunca se llama para dar de baja a
    /// alguien en el uso normal del día a día.</summary>
    Task RemoveAsync(Employee employee, CancellationToken cancellationToken = default);
}
