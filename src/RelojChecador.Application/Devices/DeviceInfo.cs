namespace RelojChecador.Application.Devices;

/// <summary>Información que el dispositivo reporta de sí mismo. Los campos son los que
/// razonablemente cualquier reloj checador puede exponer; no asume comandos específicos
/// de ningún fabricante — cada adaptador llena lo que su protocolo realmente soporte.</summary>
public sealed record DeviceInfo(
    string? SerialNumber,
    string? FirmwareVersion,
    string? Platform,
    string? FingerprintAlgorithm,
    string? Manufacturer,
    int? RegisteredUserCount,
    int? StoredAttendanceLogCount,
    int? StoredFingerprintTemplateCount);
