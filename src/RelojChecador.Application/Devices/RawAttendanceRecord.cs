namespace RelojChecador.Application.Devices;

/// <summary>
/// Una marcación tal cual la entrega el dispositivo, sin procesar. Se conserva siempre
/// como se descargó — el <c>RawPayload</c> guarda la representación original para
/// auditoría, incluso después de que el registro se reprocese o se corrija manualmente
/// en capas superiores.
/// </summary>
public sealed record RawAttendanceRecord(
    string DeviceUserPin,
    DateTime TimestampUtc,
    VerifyMethod VerifyMethod,
    int? PunchType,
    string RawPayload);
