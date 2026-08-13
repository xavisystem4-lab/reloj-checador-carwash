namespace RelojChecador.Domain.Devices;

/// <summary>
/// Estado de comunicación del dispositivo. "Unknown" es el estado inicial antes de
/// cualquier intento de diagnóstico — nunca se debe asumir "Online" por defecto.
/// </summary>
public enum DeviceStatus
{
    Unknown = 0,
    Online = 1,
    Offline = 2,
    Error = 3,
    Disabled = 4,
}
