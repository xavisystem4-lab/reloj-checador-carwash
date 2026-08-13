using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfDeviceRepository(RelojChecadorDbContext dbContext) : IDeviceRepository
{
    public Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        dbContext.Devices.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Device>> ListByBranchAsync(
        Guid branchId, CancellationToken cancellationToken = default) =>
        await dbContext.Devices
            .Where(d => d.BranchId == branchId)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Devices.OrderBy(d => d.Name).ToListAsync(cancellationToken);

    public async Task AddAsync(Device device, CancellationToken cancellationToken = default) =>
        await dbContext.Devices.AddAsync(device, cancellationToken);
}
