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

    Task<Result> ConnectAsync(DeviceConnectionInfo connection, CancellationToken cancellationToken = default);

    Task<Result> DisconnectAsync(CancellationToken cancellationToken = default);

    Task<Result<NetworkTestResult>> TestNetworkAsync(string ipAddress, CancellationToken cancellationToken = default);

    Task<Result<TcpPortTestResult>> TestTcpPortAsync(
        string ipAddress, int tcpPort, CancellationToken cancellationToken = default);

    Task<Result<DeviceInfo>> GetDeviceInformationAsync(CancellationToken cancellationToken = default);

    Task<Result<DateTime>> GetDeviceTimeAsync(CancellationToken cancellationToken = default);

    Task<Result> SetDeviceTimeAsync(DateTime utcTime, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<RawAttendanceRecord>>> DownloadAttendanceLogsAsync(
        CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<DeviceUserRecord>>> DownloadUsersAsync(CancellationToken cancellationToken = default);

    Task<Result> CreateOrUpdateUserAsync(DeviceUserRecord user, CancellationToken cancellationToken = default);

    Task<Result> DeleteUserAsync(string deviceUserPin, CancellationToken cancellationToken = default);

    Task<Result> EnableDeviceAsync(CancellationToken cancellationToken = default);

    Task<Result> DisableDeviceAsync(CancellationToken cancellationToken = default);

    Task<Result> RestartDeviceAsync(CancellationToken cancellationToken = default);

    /// <summary>Borra los registros de asistencia del dispositivo. Quien orqueste esta
    /// llamada (capa de aplicación superior) es responsable de confirmar con el usuario
    /// y verificar que exista respaldo local/nube antes de invocarla — el adaptador no
    /// toma esa decisión.</summary>
    Task<Result> ClearAttendanceLogsAsync(CancellationToken cancellationToken = default);

    Task<DeviceCapabilities> GetSupportedCapabilitiesAsync(CancellationToken cancellationToken = default);
}
