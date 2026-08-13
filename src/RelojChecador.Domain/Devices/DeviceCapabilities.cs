namespace RelojChecador.Domain.Devices;

/// <summary>
/// Capacidades que un dispositivo declara soportar realmente. El adaptador de cada
/// fabricante (ZKTeco, genérico, simulador) informa el subconjunto que aplica —
/// nunca se debe asumir que todos los relojes soportan todas las operaciones.
/// </summary>
[Flags]
public enum DeviceCapabilities
{
    None = 0,
    DownloadAttendanceLogs = 1 << 0,
    DownloadUsers = 1 << 1,
    ManageUsers = 1 << 2,
    SetDeviceTime = 1 << 3,
    RemoteRestart = 1 << 4,
    EnableDisable = 1 << 5,
    ClearAttendanceLogs = 1 << 6,
    RealTimeEvents = 1 << 7,
    FingerprintTemplateTransfer = 1 << 8,
    UserPhotoSync = 1 << 9,
}
