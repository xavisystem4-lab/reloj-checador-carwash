namespace RelojChecador.Application.Devices;

/// <summary>Un usuario tal cual vive dentro del dispositivo. "DeviceUserPin" es el
/// identificador interno del reloj — no se asume que coincide con el número de empleado
/// del negocio (ver RelojChecador.Domain.EmployeeDeviceMappings.EmployeeDeviceMapping).</summary>
public sealed record DeviceUserRecord(
    string DeviceUserPin,
    string Name,
    int PrivilegeLevel,
    bool IsEnabled);
