namespace RelojChecador.Application.Devices;

/// <summary>Resultado del primer nivel de diagnóstico: ¿la IP responde a ICMP?
/// Un ping exitoso NO significa que el reloj esté completamente conectado — solo
/// confirma que hay algo respondiendo en esa dirección de red.</summary>
public sealed record NetworkTestResult(
    bool IsReachable,
    TimeSpan? RoundTripTime,
    string? ErrorMessage,
    DateTime TestedAtUtc);

/// <summary>Segundo nivel de diagnóstico: ¿el puerto TCP configurado acepta conexiones?</summary>
public sealed record TcpPortTestResult(
    bool IsOpen,
    TimeSpan Elapsed,
    string? ErrorMessage,
    DateTime TestedAtUtc);
