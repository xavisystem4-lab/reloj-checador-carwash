using RelojChecador.Application.Common;

namespace RelojChecador.Application.Devices;

/// <summary>
/// Catálogo de errores esperados al comunicarse con un dispositivo. Tener códigos
/// estables (en vez de solo mensajes libres) permite que la UI distinga con precisión
/// en qué nivel del diagnóstico de 5 pasos falló la operación.
/// </summary>
public static class DeviceErrors
{
    public static Error InvalidIpAddress(string ipAddress) =>
        new("Device.InvalidIpAddress", $"'{ipAddress}' no es una dirección IP válida.");

    public static Error NetworkUnreachable(string ipAddress) =>
        new("Device.NetworkUnreachable", $"No hay respuesta de red (ping) desde {ipAddress}.");

    public static Error TcpPortClosed(string ipAddress, int port) =>
        new("Device.TcpPortClosed", $"El puerto TCP {port} en {ipAddress} está cerrado o filtrado.");

    public static Error ProtocolNotRecognized(string ipAddress) =>
        new("Device.ProtocolNotRecognized", $"El equipo en {ipAddress} respondió, pero no con el protocolo esperado.");

    public static Error AuthenticationFailed() =>
        new("Device.AuthenticationFailed", "La autenticación con el dispositivo fue rechazada.");

    public static Error NotConnected() =>
        new("Device.NotConnected", "No hay una conexión activa con el dispositivo. Conéctate primero.");

    public static Error OperationNotSupported(string operation) =>
        new("Device.OperationNotSupported", $"Este dispositivo no soporta la operación '{operation}'.");

    public static Error Timeout(string operation) =>
        new("Device.Timeout", $"La operación '{operation}' superó el tiempo de espera.");

    public static Error UserNotFound(string deviceUserPin) =>
        new("Device.UserNotFound", $"No existe el usuario con PIN '{deviceUserPin}' en el dispositivo.");
}
