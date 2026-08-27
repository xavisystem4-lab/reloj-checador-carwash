using RelojChecador.Application.Common;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Application.Devices;

/// <summary>
/// Contrato único que toda integración con un reloj checador debe implementar,
/// sin importar el fabricante. Ni la UI ni el motor de sincronización conocen jamás
/// los detalles del SDK, protocolo o formato de datos de una marca específica —
/// solo hablan con esta interfaz.
///
/// Los métodos <see cref="TestNetworkAsync"/> y <see cref="TestTcpPortAsync"/> son
/// independientes de la conexión (se pueden llamar sin haber invocado
/// <see cref="ConnectAsync"/> antes) porque forman parte del diagnóstico progresivo:
/// IP válida → ping → puerto TCP → protocolo/conexión → autenticación. El resto de
/// las operaciones requieren una conexión activa y devuelven
/// <see cref="DeviceErrors.NotConnected"/> si no la hay.
/// </summary>
public interface IAttendanceDeviceAdapter
{
    /// <summary>Nombre de la marca que implementa este adaptador (p. ej. "ZKTeco", "Simulador").</summary>
    string Brand { get; }

    /// <summary>
    /// Se dispara con cada marcación nueva mientras el monitoreo en tiempo real está
    /// activo (<see cref="StartRealTimeMonitoringAsync"/>) — es la vía para que la
    /// asistencia aparezca "al instante" en vez de esperar a que alguien presione
    /// "Descargar asistencias". Nunca se dispara fuera de ese modo.
    ///
    /// Quien se suscribe es responsable de volver al hilo de UI si va a tocar la UI —
    /// el adaptador no garantiza en qué hilo se invoca.
    /// </summary>
    event EventHandler<RawAttendanceRecord>? AttendancePunchReceived;

    Task<Result> ConnectAsync(DeviceConnectionInfo connection, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(CancellationToken cancellationToken = default);

    Task<Result<NetworkTestResult>> TestNetworkAsync(string ipAddress, CancellationToken cancellationToken = default);

    Task<Result<TcpPortTestResult>> TestTcpPortAsync(
        string ipAddress, int tcpPort, CancellationToken cancellationToken = default);

    Task<Result<DeviceInfo>> GetDeviceInformationAsync(CancellationToken cancellationToken = default);

    Task<Result<DateTime>> GetDeviceTimeAsync(CancellationToken cancellationToken = default);

    /// <summary>Escribe la hora en el reloj del dispositivo TAL CUAL — no se aplica
    /// conversión de zona horaria. Quien llame decide qué hora enviar (normalmente la hora
    /// local de la sucursal).</summary>
    Task<Result> SetDeviceTimeAsync(DateTime deviceTime, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RawAttendanceRecord>>> DownloadAttendanceLogsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DeviceUserRecord>>> DownloadUsersAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateOrUpdateUserAsync(DeviceUserRecord user, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(string deviceUserPin, CancellationToken cancellationToken = default);

    /// <summary>Descarga las plantillas de huella ya enroladas para un PIN (todos los dedos
    /// con datos, ver <see cref="FingerprintTemplateRecord.FingerIndex"/>) — usado para
    /// "mover" un enrolamiento a un PIN nuevo sin tener que volver a poner el dedo
    /// físicamente (ver DevicesViewModel.ChangeDeviceUserPinAsync). Lista vacía (no error)
    /// si el PIN existe pero todavía no tiene ninguna huella enrolada.</summary>
    Task<Result<IReadOnlyList<FingerprintTemplateRecord>>> DownloadUserTemplatesAsync(
        string deviceUserPin, CancellationToken cancellationToken = default);

    /// <summary>Sube UNA plantilla de huella (un dedo) a un PIN — el PIN debe existir ya
    /// (ver <see cref="CreateOrUpdateUserAsync"/>, llamar primero). Se invoca una vez por
    /// cada plantilla que devolvió <see cref="DownloadUserTemplatesAsync"/> al mover un
    /// enrolamiento de un PIN a otro.</summary>
    Task<Result> UploadUserTemplateAsync(
        string deviceUserPin, FingerprintTemplateRecord template, CancellationToken cancellationToken = default);

    Task<Result> EnableDeviceAsync(CancellationToken cancellationToken = default);

    Task<Result> DisableDeviceAsync(CancellationToken cancellationToken = default);

    Task<Result> RestartDeviceAsync(CancellationToken cancellationToken = default);

    /// <summary>Borra los registros de asistencia del dispositivo. Quien orqueste esta
    /// llamada (capa de aplicación superior) es responsable de confirmar con el usuario
    /// y verificar que exista respaldo local/nube antes de invocarla — el adaptador no
    /// toma esa decisión.</summary>
    Task<Result> ClearAttendanceLogsAsync(CancellationToken cancellationToken = default);

    Task<DeviceCapabilities> GetSupportedCapabilitiesAsync(CancellationToken cancellationToken = default);

    /// <summary>Empieza a vigilar el dispositivo para reportar cada marcación nueva casi
    /// al instante vía <see cref="AttendancePunchReceived"/>. Requiere conexión activa
    /// (<see cref="DeviceErrors.NotConnected"/> si no la hay). Seguro de llamar dos veces
    /// seguidas — la segunda no hace nada si ya está monitoreando.</summary>
    Task<Result> StartRealTimeMonitoringAsync(CancellationToken cancellationToken = default);

    /// <summary>Detiene el monitoreo iniciado por <see cref="StartRealTimeMonitoringAsync"/>.
    /// Seguro de llamar aunque no esté activo (no-op en ese caso).</summary>
    Task<Result> StopRealTimeMonitoringAsync(CancellationToken cancellationToken = default);
}
