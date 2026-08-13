using RelojChecador.Domain.Devices;

namespace RelojChecador.Application.Devices;

public interface IDeviceRepository
{
    Task<Device?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> ListByBranchAsync(Guid branchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Device>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Device device, CancellationToken cancellationToken = default);
}
