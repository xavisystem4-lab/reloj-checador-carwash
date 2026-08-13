using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfEmployeeRepository(RelojChecadorDbContext dbContext) : IEmployeeRepository
{
    public Task<Employee?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Employee?> GetByNumberAsync(EmployeeNumber number, CancellationToken cancellationToken = default) =>
        dbContext.Employees.FirstOrDefaultAsync(e => e.Number == number, cancellationToken);

    public async Task<IReadOnlyList<Employee>> ListByBranchAsync(
        Guid branchId, CancellationToken cancellationToken = default) =>
        await dbContext.Employees
            .Where(e => e.BranchId == branchId)
            .OrderBy(e => e.FullName)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Employee>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Employees.OrderBy(e => e.FullName).ToListAsync(cancellationToken);

    public async Task AddAsync(Employee employee, CancellationToken cancellationToken = default) =>
        await dbContext.Employees.AddAsync(employee, cancellationToken);
}
