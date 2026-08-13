using RelojChecador.Domain.EmployeeDeviceMappings;

namespace RelojChecador.Application.EmployeeDeviceMappings;

public interface IEmployeeDeviceMappingRepository
{
    Task<IReadOnlyList<EmployeeDeviceMapping>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default);
}
