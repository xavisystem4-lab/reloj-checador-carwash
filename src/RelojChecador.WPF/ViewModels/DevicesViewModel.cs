using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel de la pantalla de Dispositivos: alta de relojes checadores y diagnóstico
/// progresivo de conexión (5 niveles — nunca se colapsa "ping" con "conectado de verdad").
///
/// Usa el <see cref="IAttendanceDeviceAdapter"/> inyectado (hoy, <c>SimulatorDeviceAdapter</c>)
/// — el mismo contrato que usará <c>ZKTecoDeviceAdapter</c> cuando exista, así que esta
/// pantalla no cambia cuando se conecte el reloj real, solo cambia qué adaptador registra
/// el composition root (ver App.xaml.cs).
///
/// Simplificación conocida: el adaptador inyectado es una sola instancia compartida por
/// toda la app (no una sesión por dispositivo). Suficiente mientras solo hay un
/// dispositivo real de prueba; revisar esto antes de soportar varios relojes conectados
/// simultáneamente.
/// </summary>
public sealed partial class DevicesViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceRepository _deviceRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceDeviceAdapter _deviceAdapter;

    [ObservableProperty]
    private string _statusMessage = "Cargando dispositivos...";

    [ObservableProperty]
    private Device? _selectedDevice;

    // --- Diagnóstico de 5 niveles del dispositivo seleccionado ---
    // Cada uno empieza en "No verificado": nunca se asume éxito de un nivel no probado.
    [ObservableProperty] private string _ipValidResult = "No verificado";
    [ObservableProperty] private string _pingResult = "No verificado";
    [ObservableProperty] private string _portResult = "No verificado";
    [ObservableProperty] private string _protocolResult = "No verificado";
    [ObservableProperty] private string _authResult = "No verificado";
    [ObservableProperty] private bool _isConnected;

    /// <summary>¿Hay marcaciones llegando en vivo ahora mismo? Se activa solo al conectar
    /// (ver ConnectAsync) — nunca hay que presionar un botón aparte para que la asistencia
    /// aparezca al instante.</summary>
    [ObservableProperty] private bool _isMonitoringRealTime;

    // --- Información reportada por el dispositivo (solo tras "Consultar información") ---
    [ObservableProperty] private string? _infoSerialNumber;
    [ObservableProperty] private string? _infoFirmwareVersion;
    [ObservableProperty] private string? _infoPlatform;
    [ObservableProperty] private string? _infoFingerprintAlgorithm;
    [ObservableProperty] private int? _infoUserCount;
    [ObservableProperty] private int? _infoAttendanceLogCount;

    public ObservableCollection<Device> Devices { get; } = [];
    public ObservableCollection<RawAttendanceRecord> AttendanceRecords { get; } = [];
    public ObservableCollection<string> LogEntries { get; } = [];

    public DevicesViewModel(
        IDeviceRepository deviceRepository,
        IBranchRepository branchRepository,
        IUnitOfWork unitOfWork,
        IAttendanceDeviceAdapter deviceAdapter)
    {
        _deviceRepository = deviceRepository;
        _branchRepository = branchRepository;
        _unitOfWork = unitOfWork;
        _deviceAdapter = deviceAdapter;

        // El adaptador es Singleton (una sola instancia para toda la app — ver comentario de
        // clase) pero este ViewModel es Scoped (una instancia por ventana); por eso hay que
        // desuscribirse explícitamente en Dispose(), o cada ventana nueva dejaría un
        // manejador colgado apuntando a un ViewModel ya descartado.
        _deviceAdapter.AttendancePunchReceived += OnAttendancePunchReceived;
    }

    public void Dispose() => _deviceAdapter.AttendancePunchReceived -= OnAttendancePunchReceived;

    /// <summary>Se invoca desde el hilo del adaptador (background), nunca desde el de UI —
    /// por eso todo lo que toca las ObservableCollection/propiedades se reenvía al
    /// Dispatcher de WPF antes de tocarlas.</summary>
    private void OnAttendancePunchReceived(object? sender, RawAttendanceRecord record)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AttendanceRecords.Insert(0, record);
            AppendLog($"🟢 Marcación en vivo — PIN {record.DeviceUserPin} · {record.TimestampUtc:HH:mm:ss} · {record.VerifyMethod}");
        });
    }

    public async Task InitializeAsync()
    {
        try
        {
            var devices = await _deviceRepository.ListAsync();
            Devices.Clear();
            foreach (var device in devices)
            {
                Devices.Add(device);
            }

            RefreshStatusMessage();
            SelectedDevice = Devices.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de dispositivos.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

    public async Task<string?> CreateDeviceAsync(
        string name, string brand, string model, string ipAddress, int tcpPort, Guid branchId,
        string timeZoneId, string? serialNumber, string? macAddress)
    {
        try
        {
            var device = Device.Register(name, brand, model, ipAddress, tcpPort, branchId, timeZoneId, serialNumber, macAddress);
            await _deviceRepository.AddAsync(device);
            await _unitOfWork.SaveChangesAsync();

            Devices.Add(device);
            SelectedDevice = device;
            RefreshStatusMessage();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            Log.Warning(ex, "No se pudo guardar el dispositivo (Name={Name}, Ip={Ip})", name, ipAddress);
            return "No se pudo guardar el dispositivo. Verifica los datos e intenta de nuevo.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear un dispositivo (Name={Name})", name);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    partial void OnSelectedDeviceChanged(Device? value)
    {
        // Cambiar de dispositivo reinicia el diagnóstico — nunca se hereda el resultado
        // de un dispositivo distinto. También corta la conexión/monitoreo real si estaban
        // activos: como el adaptador es una sola instancia compartida (ver comentario de
        // clase), sin esto quedaría "conectado" de fondo al reloj anterior aunque la
        // pantalla ya no lo muestre así.
        if (IsConnected)
        {
            _ = _deviceAdapter.StopRealTimeMonitoringAsync();
            _ = _deviceAdapter.DisconnectAsync();
        }

        IpValidResult = "No verificado";
        PingResult = "No verificado";
        PortResult = "No verificado";
        ProtocolResult = "No verificado";
        AuthResult = "No verificado";
        IsConnected = false;
        IsMonitoringRealTime = false;
        InfoSerialNumber = null;
        InfoFirmwareVersion = null;
        InfoPlatform = null;
        InfoFingerprintAlgorithm = null;
        InfoUserCount = null;
        InfoAttendanceLogCount = null;
        AttendanceRecords.Clear();
    }

    [RelayCommand]
    private async Task PingAsync()
    {
        if (SelectedDevice is null) return;

        var result = await _deviceAdapter.TestNetworkAsync(SelectedDevice.IpAddress);
        if (result.IsFailure)
        {
            IpValidResult = "❌ " + result.Error.Message;
            PingResult = "No verificado";
            AppendLog($"Ping fallido: {result.Error.Message}");
            return;
        }

        IpValidResult = "✅ IP válida";
        PingResult = result.Value.IsReachable
            ? $"✅ Responde ({result.Value.RoundTripTime?.TotalMilliseconds:0} ms)"
            : $"❌ Sin respuesta ({result.Value.ErrorMessage})";
        AppendLog(result.Value.IsReachable ? "Ping exitoso." : "Ping sin respuesta.");
    }

    [RelayCommand]
    private async Task TestPortAsync()
    {
        if (SelectedDevice is null) return;

        var result = await _deviceAdapter.TestTcpPortAsync(SelectedDevice.IpAddress, SelectedDevice.TcpPort);
        if (result.IsFailure)
        {
            PortResult = "❌ " + result.Error.Message;
            AppendLog($"Prueba de puerto fallida: {result.Error.Message}");
            return;
        }

        PortResult = result.Value.IsOpen
            ? $"✅ Puerto {SelectedDevice.TcpPort} abierto ({result.Value.Elapsed.TotalMilliseconds:0} ms)"
            : $"❌ Puerto cerrado o filtrado ({result.Value.ErrorMessage})";
        AppendLog(result.Value.IsOpen ? "Puerto TCP abierto." : "Puerto TCP cerrado.");
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null) return;

        var connectionInfo = new DeviceConnectionInfo(SelectedDevice.IpAddress, SelectedDevice.TcpPort);
        var result = await _deviceAdapter.ConnectAsync(connectionInfo);

        if (result.IsFailure)
        {
            ProtocolResult = "No verificado";
            AuthResult = "❌ " + result.Error.Message;
            IsConnected = false;
            AppendLog($"Conexión fallida: {result.Error.Message}");
            return;
        }

        ProtocolResult = "✅ Protocolo reconocido";
        AuthResult = "✅ Autenticación correcta";
        IsConnected = true;
        AppendLog("Comunicación completa establecida con el dispositivo.");

        // Al conectar, arranca solo el monitoreo en vivo — no hay que presionar nada
        // aparte para que una marcación aparezca en la lista casi al instante.
        var monitorResult = await _deviceAdapter.StartRealTimeMonitoringAsync();
        IsMonitoringRealTime = monitorResult.IsSuccess;
        AppendLog(monitorResult.IsSuccess
            ? "Monitoreo en tiempo real activo: las marcaciones nuevas aparecerán solas."
            : $"No se pudo activar el monitoreo en tiempo real: {monitorResult.Error.Message}");
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        await _deviceAdapter.StopRealTimeMonitoringAsync();
        IsMonitoringRealTime = false;
        await _deviceAdapter.DisconnectAsync();
        IsConnected = false;
        AppendLog("Desconectado del dispositivo.");
    }

    [RelayCommand]
    private async Task GetDeviceInfoAsync()
    {
        var result = await _deviceAdapter.GetDeviceInformationAsync();
        if (result.IsFailure)
        {
            AppendLog($"No se pudo consultar información: {result.Error.Message}");
            return;
        }

        InfoSerialNumber = result.Value.SerialNumber;
        InfoFirmwareVersion = result.Value.FirmwareVersion;
        InfoPlatform = result.Value.Platform;
        InfoFingerprintAlgorithm = result.Value.FingerprintAlgorithm;
        InfoUserCount = result.Value.RegisteredUserCount;
        InfoAttendanceLogCount = result.Value.StoredAttendanceLogCount;
        AppendLog("Información del dispositivo actualizada.");
    }

    [RelayCommand]
    private async Task DownloadAttendanceAsync()
    {
        var result = await _deviceAdapter.DownloadAttendanceLogsAsync();
        if (result.IsFailure)
        {
            AppendLog($"No se pudo descargar asistencias: {result.Error.Message}");
            return;
        }

        AttendanceRecords.Clear();
        foreach (var record in result.Value)
        {
            AttendanceRecords.Add(record);
        }

        AppendLog($"Descarga completa: {result.Value.Count} registro(s) leído(s) desde el dispositivo.");
        // Nota: todavía no se persisten en la base local (eso requiere resolver primero la
        // conciliación con EmployeeDeviceMapping) — por ahora solo se muestran en pantalla.
    }

    private void AppendLog(string message) =>
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} — {message}");

    private void RefreshStatusMessage() =>
        StatusMessage = Devices.Count == 0
            ? "Aún no hay dispositivos registrados."
            : $"{Devices.Count} dispositivo(s) registrado(s) en la base local.";
}
