using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;
using RelojChecador.Infrastructure.Cloud;
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
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceDeviceAdapter _deviceAdapter;
    private readonly SupabaseSyncBackgroundService _syncService;

    // Reconexión automática (reportado por el usuario: "hasta que no le doy conectar...
    // no se actualiza" — sin esto, cualquier corte de red/reinicio del reloj dejaba de
    // subir marcaciones nuevas hasta que alguien entrara a Dispositivos y presionara
    // "Conectar" a mano). Este ViewModel es Scoped a un único scope que vive toda la
    // sesión de la app (ver App.xaml.cs, _mainWindowScope) — el timer sigue corriendo sin
    // importar en qué pestaña esté el usuario, no solo mientras ve Dispositivos.
    private readonly DispatcherTimer _autoReconnectTimer;
    private bool _autoReconnectSuspended;
    private bool _isConnecting;

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
        IAttendanceRepository attendanceRepository,
        IUnitOfWork unitOfWork,
        IAttendanceDeviceAdapter deviceAdapter,
        SupabaseSyncBackgroundService syncService)
    {
        _deviceRepository = deviceRepository;
        _branchRepository = branchRepository;
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
        _deviceAdapter = deviceAdapter;
        _syncService = syncService;

        // El adaptador es Singleton (una sola instancia para toda la app — ver comentario de
        // clase) pero este ViewModel es Scoped (una instancia por ventana); por eso hay que
        // desuscribirse explícitamente en Dispose(), o cada ventana nueva dejaría un
        // manejador colgado apuntando a un ViewModel ya descartado.
        _deviceAdapter.AttendancePunchReceived += OnAttendancePunchReceived;

        _autoReconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoReconnectTimer.Tick += async (_, _) => await TryAutoReconnectAsync();
        _autoReconnectTimer.Start();
    }

    public void Dispose()
    {
        _deviceAdapter.AttendancePunchReceived -= OnAttendancePunchReceived;
        _autoReconnectTimer.Stop();
    }

    /// <summary>Se ejecuta cada 15s (ver _autoReconnectTimer) y también una vez al cargar
    /// la pantalla (ver InitializeAsync). No hace nada si ya está conectado, si no hay
    /// dispositivo seleccionado, si el usuario desconectó a propósito con "Desconectar"
    /// (ver DisconnectAsync — se respeta esa decisión hasta que vuelva a presionar
    /// "Conectar"), o si ya hay un intento de conexión en curso.</summary>
    private async Task TryAutoReconnectAsync()
    {
        if (SelectedDevice is null || IsConnected || _autoReconnectSuspended || _isConnecting)
        {
            return;
        }

        AppendLog("🔄 Reintentando conectar automáticamente...");
        await ConnectAsync();
    }

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

        // Fire-and-forget deliberado: el evento del adaptador es síncrono (no se puede
        // "esperar" desde aquí sin bloquear su hilo de sondeo) y guardar en SQLite no debe
        // frenar la siguiente marcación. Los errores se registran en la bitácora en vez de
        // perderse en silencio — ver PersistAttendanceAsync.
        _ = PersistAndTriggerSyncAsync(record, source: "tiempo real");
    }

    /// <summary>Guarda la marcación y, si fue realmente nueva (no un duplicado), dispara
    /// de inmediato un ciclo de sincronización con Supabase — así el Dashboard la ve casi
    /// al instante en vez de esperar hasta 10s (IntervalSeconds) al siguiente ciclo
    /// automático. Pedido explícito del usuario: "en cuanto... hay un evento, que lo
    /// comunique de inmediato... con la nube para actualizar el dashboard". El propio
    /// TriggerSyncNowAsync ya tiene su candado (SupabaseSyncBackgroundService._runLock)
    /// contra solaparse con el ciclo automático o con otra marcación llegando casi a la
    /// vez, así que no hace falta ninguna protección extra aquí.</summary>
    private async Task PersistAndTriggerSyncAsync(RawAttendanceRecord record, string source)
    {
        var isNew = await PersistAttendanceAsync(record, source);
        if (isNew)
        {
            await _syncService.TriggerSyncNowAsync();
        }
    }

    /// <summary>Traduce y guarda una marcación cruda del adaptador como Attendance local,
    /// con deduplicación (la misma marcación puede llegar tanto por el monitoreo en tiempo
    /// real como por una descarga manual posterior — ver IAttendanceRepository.ExistsAsync).
    /// Requiere que <see cref="SelectedDevice"/> siga siendo el dispositivo que reportó el
    /// registro para resolver DeviceId/BranchId — válido en este ViewModel porque cambiar
    /// de dispositivo detiene el monitoreo del anterior (ver OnSelectedDeviceChanged).</summary>
    private async Task<bool> PersistAttendanceAsync(RawAttendanceRecord record, string source)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            AppendLog($"⚠️ Marcación recibida ({source}) sin dispositivo seleccionado — no se pudo guardar.");
            return false;
        }

        try
        {
            var alreadyExists = await _attendanceRepository.ExistsAsync(
                device.Id, record.DeviceUserPin, record.TimestampUtc);

            // Cualquier marcación que llega por el monitoreo en tiempo real (nueva o
            // duplicada) es evidencia fresca de que el dispositivo sigue comunicándose —
            // sin esto, LastCommunicationAtUtc solo se actualizaba al presionar "Conectar"
            // a mano (ver ConnectAsync/TryPersistCommunicationResultAsync), así que el
            // indicador "Conectado" del Dashboard (basado en ese campo, ver
            // dashboard/app.js DEVICE_ONLINE_THRESHOLD_MINUTES) expiraba a los 5 minutos
            // del último "Conectar" manual aunque el reloj siguiera mandando marcaciones
            // con total normalidad — reportado por el usuario como "a veces se desconecta".
            device.RecordSuccessfulCommunication(DateTime.UtcNow);

            if (alreadyExists)
            {
                await _unitOfWork.SaveChangesAsync();
                return false;
            }

            var attendance = Attendance.Create(
                deviceId: device.Id,
                branchId: device.BranchId,
                deviceUserPin: record.DeviceUserPin,
                timestampUtc: record.TimestampUtc,
                verifyMethod: MapVerifyMethod(record.VerifyMethod),
                punchType: record.PunchType,
                rawPayload: record.RawPayload);

            await _attendanceRepository.AddAsync(attendance);
            await _unitOfWork.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException ex)
        {
            // El índice único (DeviceId, DeviceUserPin, TimestampUtc) es la garantía real
            // contra duplicados ante una carrera entre el sondeo en tiempo real y una
            // descarga manual simultánea — el ExistsAsync de arriba es solo la vía rápida
            // que evita la mayoría de los intentos, no la única defensa.
            Log.Warning(ex, "No se pudo guardar la marcación (posible duplicado): PIN={Pin}, DeviceId={DeviceId}",
                record.DeviceUserPin, device.Id);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al guardar una marcación: PIN={Pin}, DeviceId={DeviceId}",
                record.DeviceUserPin, device.Id);
            AppendLog($"⚠️ No se pudo guardar la marcación de PIN {record.DeviceUserPin}: {ex.Message}");
            return false;
        }
    }

    private static AttendanceVerifyMethod MapVerifyMethod(VerifyMethod method) => method switch
    {
        VerifyMethod.Fingerprint => AttendanceVerifyMethod.Fingerprint,
        VerifyMethod.Password => AttendanceVerifyMethod.Password,
        VerifyMethod.Card => AttendanceVerifyMethod.Card,
        VerifyMethod.Face => AttendanceVerifyMethod.Face,
        _ => AttendanceVerifyMethod.Unknown,
    };

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

            // Primer intento inmediato al abrir la app — sin esto, el usuario tendría que
            // esperar hasta 15s (el intervalo de _autoReconnectTimer) para la primera
            // conexión automática, en vez de verla arrancar de inmediato.
            await TryAutoReconnectAsync();
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

        // El dispositivo recién seleccionado empieza sin suspensión — un "Desconectar"
        // sobre el dispositivo ANTERIOR no debe impedir que este nuevo se autoconecte.
        _autoReconnectSuspended = false;
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
        if (SelectedDevice is null || _isConnecting) return;

        // Presionar "Conectar" (a mano o vía el reintento automático) siempre reactiva el
        // auto-reconnect, incluso si venía suspendido por un "Desconectar" manual previo —
        // es la forma natural de decirle a la app "vuelve a intentar mantenerte conectado".
        _autoReconnectSuspended = false;
        _isConnecting = true;
        try
        {
            var connectionInfo = new DeviceConnectionInfo(SelectedDevice.IpAddress, SelectedDevice.TcpPort);
            var result = await _deviceAdapter.ConnectAsync(connectionInfo);

            if (result.IsFailure)
            {
                ProtocolResult = "No verificado";
                AuthResult = "❌ " + result.Error.Message;
                IsConnected = false;
                AppendLog($"Conexión fallida: {result.Error.Message}");

                // Se registra el intento fallido igual que el éxito de abajo — así el estado
                // "Conectado"/"Desconectado" que ve el Dashboard (basado en LastCommunicationAtUtc
                // y Status, sincronizados a Supabase en cada ciclo) refleja la realidad en vez de
                // quedarse pegado en el último éxito para siempre.
                await TryPersistCommunicationResultAsync(succeeded: false);
                return;
            }

            ProtocolResult = "✅ Protocolo reconocido";
            AuthResult = "✅ Autenticación correcta";
            IsConnected = true;
            AppendLog("Comunicación completa establecida con el dispositivo.");
            await TryPersistCommunicationResultAsync(succeeded: true);

            // Al conectar, arranca solo el monitoreo en vivo — no hay que presionar nada
            // aparte para que una marcación aparezca en la lista casi al instante.
            var monitorResult = await _deviceAdapter.StartRealTimeMonitoringAsync();
            IsMonitoringRealTime = monitorResult.IsSuccess;
            AppendLog(monitorResult.IsSuccess
                ? "Monitoreo en tiempo real activo: las marcaciones nuevas aparecerán solas."
                : $"No se pudo activar el monitoreo en tiempo real: {monitorResult.Error.Message}");
        }
        finally
        {
            _isConnecting = false;
        }
    }

    /// <summary>Deja constancia en el dispositivo local (Device.RecordSuccessfulCommunication/
    /// RecordFailedCommunication, ya existían en el dominio pero nada los llamaba todavía)
    /// de si la conexión real funcionó. Se guarda con IUnitOfWork.SaveChangesAsync —
    /// SelectedDevice ya está bajo seguimiento de EF Core (viene de _deviceRepository.ListAsync(),
    /// sin AsNoTracking), así que no hace falta un método "UpdateAsync" aparte. Nunca deja
    /// que un fallo al guardar tumbe la pantalla — es un detalle de estado, no algo que
    /// deba interrumpir al usuario si falla.</summary>
    private async Task TryPersistCommunicationResultAsync(bool succeeded)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            return;
        }

        try
        {
            if (succeeded)
            {
                device.RecordSuccessfulCommunication(DateTime.UtcNow);
            }
            else
            {
                device.RecordFailedCommunication();
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo guardar el estado de comunicación del dispositivo {DeviceId}", device.Id);
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        // Se respeta la decisión explícita del usuario: el auto-reconnect (cada 15s, ver
        // _autoReconnectTimer) queda suspendido hasta que vuelva a presionar "Conectar" —
        // si no, este botón no serviría para nada, el timer reconectaría solo segundos después.
        _autoReconnectSuspended = true;
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
        var savedCount = 0;
        // Más reciente arriba — el dispositivo entrega los registros en el orden en que
        // los tiene almacenados internamente (normalmente de llegada), no por fecha; se
        // ordena explícitamente antes de mostrarlos para que la marcación más nueva quede
        // primera sin depender de esa suposición.
        foreach (var record in result.Value.OrderByDescending(r => r.TimestampUtc))
        {
            AttendanceRecords.Add(record);
            if (await PersistAttendanceAsync(record, source: "descarga manual"))
            {
                savedCount++;
            }
        }

        AppendLog($"Descarga completa: {result.Value.Count} registro(s) leído(s) desde el dispositivo " +
                   $"({savedCount} nuevo(s) guardado(s) en la base local, el resto ya existía).");

        // Un solo ciclo de sync al final del lote (no uno por registro, a diferencia de
        // PersistAndTriggerSyncAsync para tiempo real) — una descarga manual puede traer
        // cientos de registros de golpe, y TriggerSyncNowAsync ya sube TODO lo pendiente
        // en un ciclo (ver PushAttendancesIncrementalAsync), así que repetirlo por cada
        // fila no adelantaría nada, solo generaría llamadas redundantes a Supabase.
        if (savedCount > 0)
        {
            await _syncService.TriggerSyncNowAsync();
        }
    }

    /// <summary>Escribe la hora LOCAL de esta PC en el reloj del dispositivo — no se envía
    /// UTC: el reloj no aplica ninguna conversión de zona horaria, así que lo que se
    /// escriba aquí es exactamente lo que la gente va a ver físicamente en la pantalla del
    /// dispositivo y lo que quedará sellado en las marcaciones nuevas.</summary>
    [RelayCommand]
    private async Task SyncDeviceTimeAsync()
    {
        var beforeResult = await _deviceAdapter.GetDeviceTimeAsync();
        var before = beforeResult.IsSuccess ? beforeResult.Value.ToString("dd/MM/yyyy HH:mm:ss") : "desconocida";

        var result = await _deviceAdapter.SetDeviceTimeAsync(DateTime.Now);
        if (result.IsFailure)
        {
            AppendLog($"No se pudo sincronizar la hora del dispositivo: {result.Error.Message}");
            return;
        }

        AppendLog($"Hora del dispositivo sincronizada (antes: {before} → ahora: {DateTime.Now:dd/MM/yyyy HH:mm:ss}).");
    }

    private void AppendLog(string message) =>
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} — {message}");

    private void RefreshStatusMessage() =>
        StatusMessage = Devices.Count == 0
            ? "Aún no hay dispositivos registrados."
            : $"{Devices.Count} dispositivo(s) registrado(s) en la base local.";
}
