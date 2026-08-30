using RelojChecador.Domain.EmployeeDeviceMappings;

namespace RelojChecador.Application.EmployeeDeviceMappings;

public interface IEmployeeDeviceMappingRepository
{
    Task<IReadOnlyList<EmployeeDeviceMapping>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Usado para corregir el PIN de un vínculo ya existente (ver
    /// EmployeeDeviceMapping.UpdatePin) — se necesita la entidad trackeada por el
    /// DbContext, no solo un elemento suelto de ListAsync.</summary>
    Task<EmployeeDeviceMapping?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>Vínculos de un empleado en particular — usado para limpiarlos antes de un
    /// borrado físico (ver IEmployeeRepository.RemoveAsync).</summary>
    Task<IReadOnlyList<EmployeeDeviceMapping>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    Task RemoveAsync(EmployeeDeviceMapping mapping, CancellationToken cancellationToken = default);
}
