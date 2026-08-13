using System.Net;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Infrastructure.Devices.Simulator;

/// <summary>
/// Implementación completa y determinista de <see cref="IAttendanceDeviceAdapter"/> que
/// no requiere hardware. Se usa para desarrollar y probar toda la aplicación (UI, motor
/// de sincronización, reportes) antes de tener el adaptador real de ZKTeco terminado, y
/// para pruebas automatizadas que no dependen de un reloj físico.
///
/// Los datos de identidad del dispositivo (serie, firmware, plataforma) son los reales
/// del equipo ZKTeco F22/ID que se usará en campo, para que el diagnóstico y las
/// pantallas se vean realistas incluso sin conexión al hardware.
/// </summary>
public sealed class SimulatorDeviceAdapter : IAttendanceDeviceAdapter, IDisposable
{
    private readonly Dictionary<string, DeviceUserRecord> _users;
    private readonly List<RawAttendanceRecord> _attendanceLogs;
    private bool _isConnected;
    private bool _isEnabled = true;
    private Timer? _realTimeTimer;
    private int _realTimeTick;

    public string Brand => "Simulador";

    public event EventHandler<RawAttendanceRecord>? AttendancePunchReceived;

    /// <summary>Permite forzar fallas de red/puerto en pruebas (p. ej. simular el reloj apagado).</summary>
    public bool SimulateNetworkUnreachable { get; set; }
    public bool SimulateTcpPortClosed { get; set; }
    public bool SimulateAuthenticationFailure { get; set; }

    public SimulatorDeviceAdapter()
    {
        _users = new Dictionary<string, DeviceUserRecord>
        {
            ["1"] = new DeviceUserRecord("1", "Ana Torres", PrivilegeLevel: 0, IsEnabled: true),
            ["2"] = new DeviceUserRecord("2", "Luis Peña", PrivilegeLevel: 0, IsEnabled: true),
            ["3"] = new DeviceUserRecord("3", "Marta Gil", PrivilegeLevel: 14, IsEnabled: true),
        };

        var today = DateTime.UtcNow.Date;
        _attendanceLogs =
        [
            new RawAttendanceRecord("1", today.AddHours(8).AddMinutes(2), VerifyMethod.Fingerprint, PunchType: 0, RawPayload: "SIM|1|IN"),
            new RawAttendanceRecord("2", today.AddHours(8).AddMinutes(5), VerifyMethod.Fingerprint, PunchType: 0, RawPayload: "SIM|2|IN"),
            new RawAttendanceRecord("1", today.AddHours(17).AddMinutes(1), VerifyMethod.Fingerprint, PunchType: 1, RawPayload: "SIM|1|OUT"),
        ];
    }

    public Task<Result> ConnectAsync(DeviceConnectionInfo connection, CancellationToken cancellationToken = default)
    {
        if (SimulateNetworkUnreachable)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NetworkUnreachable(connection.IpAddress)));
        }

        if (SimulateTcpPortClosed)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.TcpPortClosed(connection.IpAddress, connection.TcpPort)));
        }

        if (SimulateAuthenticationFailure)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.AuthenticationFailed()));
        }

        _isConnected = true;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = false;
        _realTimeTimer?.Dispose();
        _realTimeTimer = null;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<NetworkTestResult>> TestNetworkAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!IPAddress.TryParse(ipAddress, out _))
        {
            return Task.FromResult(Result.Failure<NetworkTestResult>(DeviceErrors.InvalidIpAddress(ipAddress)));
        }

        var now = DateTime.UtcNow;

        if (SimulateNetworkUnreachable)
        {
            return Task.FromResult(Result.Success(new NetworkTestResult(
                IsReachable: false,
                RoundTripTime: null,
                ErrorMessage: "Tiempo de espera agotado (simulado).",
                TestedAtUtc: now)));
        }

        return Task.FromResult(Result.Success(new NetworkTestResult(
            IsReachable: true,
            RoundTripTime: TimeSpan.FromMilliseconds(18),
            ErrorMessage: null,
            TestedAtUtc: now)));
    }

    public Task<Result<TcpPortTestResult>> TestTcpPortAsync(
        string ipAddress, int tcpPort, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        if (SimulateTcpPortClosed)
        {
            return Task.FromResult(Result.Success(new TcpPortTestResult(
                IsOpen: false,
                Elapsed: TimeSpan.FromSeconds(3.2),
                ErrorMessage: $"Tiempo de espera agotado conectando al puerto {tcpPort} (simulado).",
                TestedAtUtc: now)));
        }

        return Task.FromResult(Result.Success(new TcpPortTestResult(
            IsOpen: true,
            Elapsed: TimeSpan.FromMilliseconds(45),
            ErrorMessage: null,
            TestedAtUtc: now)));
    }

    public Task<Result<DeviceInfo>> GetDeviceInformationAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure<DeviceInfo>(DeviceErrors.NotConnected()));
        }

        var info = new DeviceInfo(
            SerialNumber: "CQZ7233360308",
            FirmwareVersion: "Ver 8.0.4.3-20220708",
            Platform: "ZLM60_TFT",
            FingerprintAlgorithm: "ZKFinger VX10.0",
            Manufacturer: "ZKTECO CO., LTD. (simulado)",
            RegisteredUserCount: _users.Count,
            StoredAttendanceLogCount: _attendanceLogs.Count,
            StoredFingerprintTemplateCount: _users.Count);

        return Task.FromResult(Result.Success(info));
    }

    public Task<Result<DateTime>> GetDeviceTimeAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure<DateTime>(DeviceErrors.NotConnected()));
        }

        return Task.FromResult(Result.Success(DateTime.UtcNow));
    }

    public Task<Result> SetDeviceTimeAsync(DateTime utcTime, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        // El simulador no tiene un reloj propio que ajustar; solo confirma la operación.
        return Task.FromResult(Result.Success());
    }

    public Task<Result<IReadOnlyList<RawAttendanceRecord>>> DownloadAttendanceLogsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<RawAttendanceRecord>>(DeviceErrors.NotConnected()));
        }

        IReadOnlyList<RawAttendanceRecord> logs = _attendanceLogs.AsReadOnly();
        return Task.FromResult(Result.Success(logs));
    }

    public Task<Result<IReadOnlyList<DeviceUserRecord>>> DownloadUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<DeviceUserRecord>>(DeviceErrors.NotConnected()));
        }

        IReadOnlyList<DeviceUserRecord> users = _users.Values.ToList().AsReadOnly();
        return Task.FromResult(Result.Success(users));
    }

    public Task<Result> CreateOrUpdateUserAsync(DeviceUserRecord user, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        _users[user.DeviceUserPin] = user;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DeleteUserAsync(string deviceUserPin, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        if (!_users.Remove(deviceUserPin))
        {
            return Task.FromResult(Result.Failure(DeviceErrors.UserNotFound(deviceUserPin)));
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> EnableDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        _isEnabled = true;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> DisableDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        _isEnabled = false;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RestartDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> ClearAttendanceLogsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        _attendanceLogs.Clear();
        return Task.FromResult(Result.Success());
    }

    public Task<DeviceCapabilities> GetSupportedCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        const DeviceCapabilities all =
            DeviceCapabilities.DownloadAttendanceLogs |
            DeviceCapabilities.DownloadUsers |
            DeviceCapabilities.ManageUsers |
            DeviceCapabilities.SetDeviceTime |
            DeviceCapabilities.RemoteRestart |
            DeviceCapabilities.EnableDisable |
            DeviceCapabilities.ClearAttendanceLogs |
            DeviceCapabilities.RealTimeEvents |
            DeviceCapabilities.FingerprintTemplateTransfer |
            DeviceCapabilities.UserPhotoSync;

        return Task.FromResult(all);
    }

    public Task<Result> StartRealTimeMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        // Emite una marcación simulada cada 4s (alternando los mismos empleados de
        // _attendanceLogs), suficiente para verificar en pantalla que la UI reacciona en
        // vivo sin depender de tocar el reloj físico. No persiste nada — igual que
        // DownloadAttendanceLogsAsync, quien escuche decide qué hacer con el registro.
        _realTimeTimer ??= new Timer(_ => EmitSimulatedPunch(), null, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(4));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> StopRealTimeMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _realTimeTimer?.Dispose();
        _realTimeTimer = null;
        return Task.FromResult(Result.Success());
    }

    private void EmitSimulatedPunch()
    {
        var pin = (_realTimeTick % 2 == 0) ? "1" : "2";
        var punchType = (_realTimeTick % 4 < 2) ? 0 : 1; // IN, IN, OUT, OUT, IN, ...
        _realTimeTick++;

        var record = new RawAttendanceRecord(
            pin, DateTime.UtcNow, VerifyMethod.Fingerprint, punchType, RawPayload: $"SIM-RT|{pin}|{punchType}");
        AttendancePunchReceived?.Invoke(this, record);
    }

    public void Dispose() => _realTimeTimer?.Dispose();

    /// <summary>¿El dispositivo simulado está actualmente habilitado? Expuesto para pruebas.</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>¿Hay una "conexión" activa en este momento? Expuesto para pruebas.</summary>
    public bool IsConnected => _isConnected;
}
