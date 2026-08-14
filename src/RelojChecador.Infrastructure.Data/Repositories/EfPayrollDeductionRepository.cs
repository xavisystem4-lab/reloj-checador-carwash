using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Payroll;
using RelojChecador.Domain.Payroll;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfPayrollDeductionRepository(RelojChecadorDbContext dbContext) : IPayrollDeductionRepository
{
    public async Task<IReadOnlyList<PayrollDeduction>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.PayrollDeductions.ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PayrollDeduction>> ListByWeekAsync(DateOnly weekStart, CancellationToken cancellationToken = default) =>
        await dbContext.PayrollDeductions.Where(d => d.WeekStart == weekStart).ToListAsync(cancellationToken);

    public Task<PayrollDeduction?> GetByEmployeeAndWeekAsync(Guid employeeId, DateOnly weekStart, CancellationToken cancellationToken = default) =>
        dbContext.PayrollDeductions.FirstOrDefaultAsync(d => d.EmployeeId == employeeId && d.WeekStart == weekStart, cancellationToken);

    public async Task AddAsync(PayrollDeduction deduction, CancellationToken cancellationToken = default) =>
        await dbContext.PayrollDeductions.AddAsync(deduction, cancellationToken);
}
