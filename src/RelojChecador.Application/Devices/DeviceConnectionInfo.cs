namespace RelojChecador.Application.Devices;

/// <summary>Datos necesarios para que un adaptador intente conectarse a un dispositivo.
/// No incluye la contraseña/clave de comunicación en texto plano de forma persistente —
/// quien construye este objeto la obtiene de Windows Credential Manager justo antes de usarla.</summary>
public sealed record DeviceConnectionInfo(
    string IpAddress,
    int TcpPort,
    string? CommunicationPassword = null,
    string? MachineNumber = null,
    int TimeoutMilliseconds = 5000);
