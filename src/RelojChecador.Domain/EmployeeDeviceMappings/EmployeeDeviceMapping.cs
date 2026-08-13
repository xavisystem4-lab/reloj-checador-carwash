using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.EmployeeDeviceMappings;

/// <summary>
/// Vincula un Employee con el identificador ("PIN") que un Device específico usa
/// internamente para reconocerlo. Existe para evitar duplicar empleados al descargar
/// usuarios desde varios relojes: nunca se asume que el PIN del dispositivo coincide
/// con el número de empleado del negocio.
/// </summary>
public sealed class EmployeeDeviceMapping : Entity
{
    public Guid EmployeeId { get; private set; }
    public Guid DeviceId { get; private set; }
    public string DeviceUserPin { get; private set; } = null!;
    public DateTime EnrolledAtUtc { get; private set; }

    private EmployeeDeviceMapping()
    {
        // Constructor privado para EF Core.
    }

    public static EmployeeDeviceMapping Create(Guid employeeId, Guid deviceId, string deviceUserPin)
    {
        Guard.AgainstEmptyGuid(employeeId, nameof(employeeId));
        Guard.AgainstEmptyGuid(deviceId, nameof(deviceId));
        Guard.AgainstNullOrWhiteSpace(deviceUserPin, nameof(deviceUserPin));

        return new EmployeeDeviceMapping
        {
            Id = Guid.CreateVersion7(),
            EmployeeId = employeeId,
            DeviceId = deviceId,
            DeviceUserPin = deviceUserPin.Trim(),
            EnrolledAtUtc = DateTime.UtcNow,
        };
    }
}
