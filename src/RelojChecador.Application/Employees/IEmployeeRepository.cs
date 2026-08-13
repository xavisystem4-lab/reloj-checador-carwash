using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Employees;

public interface IEmployeeRepository
{
    Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Employee?> GetByNumberAsync(EmployeeNumber number, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Employee>> ListByBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Employee employee, CancellationToken cancellationToken = default);
}
