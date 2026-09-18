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

    /// <summary>Traspasa este vínculo (mismo dispositivo y mismo PIN) a OTRO empleado — caso
    /// real: tras "Reemplazar catálogo" los empleados anteriores quedan dados de baja pero
    /// siguen "dueños" de su PIN en el reloj, y sus marcaciones también; el empleado nuevo de
    /// la misma persona necesita heredar ese PIN. Se reasigna la fila (mismo Id) en vez de
    /// borrar y crear otra porque la nube nunca recibe un DELETE y el índice único
    /// (DeviceId, DeviceUserPin) de Supabase rechazaría una fila nueva con el mismo PIN.</summary>
    public void ReassignEmployee(Guid newEmployeeId)
    {
        Guard.AgainstEmptyGuid(newEmployeeId, nameof(newEmployeeId));
        EmployeeId = newEmployeeId;
    }

    /// <summary>Corrige el PIN de un vínculo ya existente — caso real: el usuario captura
    /// un PIN equivocado al vincular (p. ej. el número de empleado en vez del PIN real del
    /// reloj) y no puede simplemente "vincular de nuevo" porque el índice único
    /// (DeviceId, EmployeeId) se lo impide (ver EmployeeDeviceMappingConfiguration) — sin
    /// esto, no había ninguna forma de corregirlo desde la UI.</summary>
    public void UpdatePin(string newDeviceUserPin)
    {
        Guard.AgainstNullOrWhiteSpace(newDeviceUserPin, nameof(newDeviceUserPin));
        DeviceUserPin = newDeviceUserPin.Trim();
    }
}
