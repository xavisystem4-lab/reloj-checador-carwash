using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Domain.EmployeeDeviceMappings;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfEmployeeDeviceMappingRepository(RelojChecadorDbContext dbContext) : IEmployeeDeviceMappingRepository
{
    public async Task<IReadOnlyList<EmployeeDeviceMapping>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.EmployeeDeviceMappings.ToListAsync(cancellationToken);

    public Task<EmployeeDeviceMapping?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.EmployeeDeviceMappings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task AddAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default) =>
        await dbContext.EmployeeDeviceMappings.AddAsync(mapping, cancellationToken);

    public async Task<IReadOnlyList<EmployeeDeviceMapping>> ListByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default) =>
        await dbContext.EmployeeDeviceMappings.Where(m => m.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public Task RemoveAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default)
    {
        dbContext.EmployeeDeviceMappings.Remove(mapping);
        return Task.CompletedTask;
    }
}
