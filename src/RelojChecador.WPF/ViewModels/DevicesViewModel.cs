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
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.EmployeeDeviceMappings;
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
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAttendanceDeviceAdapter _deviceAdapter;
    private readonly SupabaseSyncBackgroundService _syncService;
    private readonly RemoteSyncRequestCoordinator _remoteSyncCoordinator;

    // Guardia de reentrancia para "Enviar empleados al reloj" — evita que un doble clic (o
    // que el usuario navegue de pestaña y vuelva) dispare dos lotes de SSR_SetUserInfo al
    // mismo tiempo, mismo criterio que _isDownloading/_isConnecting.
    private bool _isSendingEmployees;

    // Reconexión automática (reportado por el usuario: "hasta que no le doy conectar...
    // no se actualiza" — sin esto, cualquier corte de red/reinicio del reloj dejaba de
    // subir marcaciones nuevas hasta que alguien entrara a Dispositivos y presionara
    // "Conectar" a mano). Este ViewModel es Scoped a un único scope que vive toda la
    // sesión de la app (ver App.xaml.cs, _mainWindowScope) — el timer sigue corriendo sin
    // importar en qué pestaña esté el usuario, no solo mientras ve Dispositivos.
    private readonly DispatcherTimer _autoReconnectTimer;
    private bool _autoReconnectSuspended;
    private bool _isConnecting;

    // Descarga automática (pedido explícito del usuario: que "el botón de descarga
    // asistencia se actualice por sí solo" cada 5-10s, sin depender de que el monitoreo en
    // tiempo real esté funcionando ni de que alguien presione el botón — ni siquiera de una
    // señal remota del Dashboard). Complementa, no reemplaza, al monitoreo en tiempo real
    // (AttendancePunchReceived) y a la solicitud remota (RemoteSyncRequestCoordinator): es
    // una tercera vía, puramente local, que no depende de que ninguna de las otras dos esté
    // funcionando. Nunca duplica marcaciones — PersistAttendanceAsync ya deduplica (ExistsAsync
    // + índice único como respaldo), así que solapar con tiempo real es seguro.
    private readonly DispatcherTimer _autoDownloadTimer;
    private bool _isDownloading;

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
        IEmployeeRepository employeeRepository,
        IEmployeeDeviceMappingRepository mappingRepository,
        IUnitOfWork unitOfWork,
        IAttendanceDeviceAdapter deviceAdapter,
        SupabaseSyncBackgroundService syncService,
        RemoteSyncRequestCoordinator remoteSyncCoordinator)
    {
        _deviceRepository = deviceRepository;
        _branchRepository = branchRepository;
        _attendanceRepository = attendanceRepository;
        _employeeRepository = employeeRepository;
        _mappingRepository = mappingRepository;
        _unitOfWork = unitOfWork;
        _deviceAdapter = deviceAdapter;
        _syncService = syncService;
        _remoteSyncCoordinator = remoteSyncCoordinator;

        // El adaptador es Singleton (una sola instancia para toda la app — ver comentario de
        // clase) pero este ViewModel es Scoped (una instancia por ventana); por eso hay que
        // desuscribirse explícitamente en Dispose(), o cada ventana nueva dejaría un
        // manejador colgado apuntando a un ViewModel ya descartado.
        _deviceAdapter.AttendancePunchReceived += OnAttendancePunchReceived;

        _autoReconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoReconnectTimer.Tick += async (_, _) => await TryAutoReconnectAsync();
        _autoReconnectTimer.Start();

        _autoDownloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoDownloadTimer.Tick += async (_, _) => await TryAutoDownloadAsync();
        _autoDownloadTimer.Start();

        // Igual que _deviceAdapter.AttendancePunchReceived: se dispara en el hilo del
        // RemoteSyncRequestPollingService, no en el de UI — el handler hace su propio
        // marshaling (ver OnRemoteSyncRequested). RemoteSyncRequestCoordinator es
        // Singleton, así que hay que desuscribirse en Dispose() por la misma razón que el
        // adaptador: este ViewModel es Scoped, un ViewModel nuevo por ventana no debe
        // dejar un manejador colgado apuntando a uno ya descartado.
        _remoteSyncCoordinator.SyncRequested += OnRemoteSyncRequested;
    }

    public void Dispose()
    {
        _deviceAdapter.AttendancePunchReceived -= OnAttendancePunchReceived;
        _remoteSyncCoordinator.SyncRequested -= OnRemoteSyncRequested;
        _autoReconnectTimer.Stop();
        _autoDownloadTimer.Stop();
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

    /// <summary>Se ejecuta cada 10s (ver _autoDownloadTimer) mientras haya un dispositivo
    /// conectado — descarga del reloj y sube a la nube exactamente igual que el botón
    /// "Descargar asistencias", sin que nadie tenga que presionarlo. Deliberadamente
    /// silenciosa cuando no hay nada nuevo (no deja rastro en la bitácora cada 10s aunque
    /// no haya pasado nada) — solo se registra cuando de verdad hay marcaciones nuevas o
    /// algo salió mal, igual que el resto de los timers automáticos de este ViewModel.</summary>
    private async Task TryAutoDownloadAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        var (success, _, totalRead, savedCount) = await DownloadAttendanceCoreAsync();
        if (!success)
        {
            // Sin log a propósito: un fallo puntual de una descarga automática cada 10s no
            // debe inundar la bitácora — si el dispositivo realmente se desconectó, eso ya
            // se refleja en IsConnected y lo recoge el auto-reconnect (cada 15s).
            return;
        }

        if (savedCount > 0)
        {
            AppendLog($"🔄 Descarga automática: {savedCount} marcación(es) nueva(s) de {totalRead} leída(s).");
            await _syncService.TriggerSyncNowAsync();
        }
    }

    /// <summary>Se invoca desde el hilo del adaptador (background), nunca desde el de UI —
    /// por eso todo lo que toca las ObservableCollection/propiedades se reenvía al
    /// Dispatcher de WPF antes de tocarlas. Esto incluye el fire-and-forget de abajo: un
    /// crash real reportado por el usuario (NotSupportedException "CollectionView no
    /// admite cambios... de un subproceso distinto del subproceso Dispatcher" →
    /// UnobservedTaskException, tumbaba la app entera) venía de un caso hermano de este
    /// mismo patrón (ver OnRemoteSyncRequested) donde el fire-and-forget quedaba FUERA del
    /// bloque marshalizado — PersistAttendanceAsync también llama AppendLog en sus rutas de
    /// error, así que tenía que quedar dentro del mismo Dispatcher.Invoke.</summary>
    private void OnAttendancePunchReceived(object? sender, RawAttendanceRecord record)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            AttendanceRecords.Insert(0, record);
            AppendLog($"🟢 Marcación en vivo — PIN {record.DeviceUserPin} · {record.TimestampUtc:HH:mm:ss} · {record.VerifyMethod}");

            // Fire-and-forget deliberado: no se puede "esperar" aquí sin bloquear el hilo de
            // UI hasta que termine de guardar en SQLite y sincronizar con la nube — pero sí
            // debe INICIARSE en el hilo de UI (dentro de este Invoke) para que sus `await`
            // reanuden aquí mismo vía el SynchronizationContext del Dispatcher, no en el hilo
            // de sondeo del adaptador. Los errores se registran en la bitácora en vez de
            // perderse en silencio — ver PersistAttendanceAsync.
            _ = PersistAndTriggerSyncAsync(record, source: "tiempo real");
        });
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

    /// <summary>Corrige los datos de un dispositivo ya registrado — siempre opera sobre
    /// <see cref="SelectedDevice"/> (el botón "Editar dispositivo" vive en el panel de
    /// diagnóstico del dispositivo seleccionado, no en la lista), así que tras guardar se
    /// recarga la lista completa y se vuelve a seleccionar el mismo dispositivo por Id —
    /// mismo patrón de recarga completa que EmployeesViewModel.ReloadAsync. Reasignar
    /// SelectedDevice dispara OnSelectedDeviceChanged, que reinicia el diagnóstico y corta
    /// cualquier conexión en curso — correcto a propósito: si se editó la IP/puerto, la
    /// conexión anterior ya no aplica: el auto-reconnect (15s) la retoma sola, y su
    /// resultado (éxito o fallo) empuja el estado a Supabase de inmediato por su cuenta
    /// (ver TryPersistCommunicationResultAsync). Aquí se empuja además el cambio de datos
    /// en sí (nombre/IP/puerto/etc.) sin esperar a que eso ocurra — pedido explícito del
    /// usuario: "cada vez que yo cambie los parámetros... este siempre debe actualizar en
    /// Supabase", no solo cuando además se reconecta con éxito.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateDeviceAsync(
        Guid deviceId, string name, string brand, string model, string ipAddress, int tcpPort,
        Guid branchId, string timeZoneId, string? serialNumber, string? macAddress)
    {
        try
        {
            var device = await _deviceRepository.GetByIdAsync(deviceId);
            if (device is null)
            {
                return "No se encontró el dispositivo — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            device.UpdateDetails(name, brand, model, branchId, timeZoneId, serialNumber, macAddress);
            device.UpdateNetworkSettings(ipAddress, tcpPort);
            await _unitOfWork.SaveChangesAsync();
            await _syncService.TriggerSyncNowAsync();

            var devices = await _deviceRepository.ListAsync();
            Devices.Clear();
            foreach (var d in devices)
            {
                Devices.Add(d);
            }

            RefreshStatusMessage();
            SelectedDevice = Devices.FirstOrDefault(d => d.Id == deviceId) ?? Devices.FirstOrDefault();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            Log.Warning(ex, "No se pudo actualizar el dispositivo (DeviceId={DeviceId})", deviceId);
            return "No se pudo guardar el dispositivo. Verifica los datos e intenta de nuevo.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al actualizar el dispositivo {DeviceId}", deviceId);
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
    /// deba interrumpir al usuario si falla.
    ///
    /// Tras guardar, empuja el cambio a Supabase de inmediato (TriggerSyncNowAsync) — pedido
    /// explícito del usuario ("cada vez que yo cambie los parámetros... este siempre debe
    /// actualizar en Supabase y mostrar conectado"): reportó ver el Dashboard mostrando
    /// "Desconectado (hace 8h)" mientras la app de escritorio ya estaba "Conectado (hace 0s)".
    /// Antes había que esperar hasta IntervalSeconds (el ciclo automático) para que el
    /// Dashboard reflejara un Conectar/Desconectar/reconexión real; ahora es casi al
    /// instante, igual criterio que PersistAndTriggerSyncAsync para marcaciones nuevas.</summary>
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
            await _syncService.TriggerSyncNowAsync();
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

        // Antes esto no tocaba Device.Status ni la nube — un "Desconectar" manual dejaba a
        // Supabase (y por lo tanto al Dashboard) mostrando "Conectado" indefinidamente hasta
        // el siguiente fallo de reconexión automática. Reutiliza el mismo helper que
        // ConnectAsync para que el Dashboard refleje el desconecte manual de inmediato.
        await TryPersistCommunicationResultAsync(succeeded: false);
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
        var (success, error, totalRead, savedCount) = await DownloadAttendanceCoreAsync();
        if (!success)
        {
            AppendLog($"No se pudo descargar asistencias: {error}");
            return;
        }

        AppendLog($"Descarga completa: {totalRead} registro(s) leído(s) desde el dispositivo " +
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

    /// <summary>Núcleo reutilizable de "Descargar asistencias": lee del dispositivo,
    /// refresca <see cref="AttendanceRecords"/> y persiste cada registro nuevo en la base
    /// local. Reutilizado por tres llamadores — el <c>[RelayCommand]</c> de arriba (botón
    /// "Descargar asistencias"), <see cref="TryAutoDownloadAsync"/> (descarga automática
    /// cada 10s) y <see cref="ProcessRemoteSyncRequestAsync"/> (solicitud remota
    /// "Actualizar asistencias" desde el Dashboard) — ninguno duplica esta lógica.
    /// Guardia de reentrancia compartida (<see cref="_isDownloading"/>): con la descarga
    /// automática corriendo cada 10s, es real que dos de estos tres caminos coincidan si
    /// el dispositivo tarda en responder — sin esto, dos descargas simultáneas pisarían
    /// AttendanceRecords entre sí.</summary>
    private async Task<(bool Success, string? Error, int TotalRead, int SavedCount)> DownloadAttendanceCoreAsync()
    {
        if (_isDownloading)
        {
            return (false, "Ya hay una descarga en curso.", 0, 0);
        }

        _isDownloading = true;
        try
        {
            var result = await _deviceAdapter.DownloadAttendanceLogsAsync();
            if (result.IsFailure)
            {
                return (false, result.Error.Message, 0, 0);
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
                if (await PersistAttendanceAsync(record, source: "descarga"))
                {
                    savedCount++;
                }
            }

            return (true, null, result.Value.Count, savedCount);
        }
        finally
        {
            _isDownloading = false;
        }
    }

    /// <summary>Se dispara cuando <see cref="RemoteSyncRequestCoordinator"/> detecta una
    /// solicitud "Actualizar asistencias" pendiente desde el Dashboard — evento de hilo de
    /// fondo (el hilo de sondeo de RemoteSyncRequestPollingService, nunca el de UI).
    ///
    /// CRASH REAL reportado por el usuario (v1.17.1): antes solo el AppendLog de aquí se
    /// marshalizaba al Dispatcher; el fire-and-forget de ProcessRemoteSyncRequestAsync
    /// quedaba fuera, así que TODO lo que esa cadena toca (AppendLog/LogEntries,
    /// ConnectAsync y sus propiedades observables, DownloadAttendanceCoreAsync y
    /// AttendanceRecords) corría en el hilo de sondeo — WPF lo rechaza con
    /// NotSupportedException ("CollectionView no admite cambios... de un subproceso
    /// distinto del subproceso Dispatcher"), y como nadie observaba esa excepción (Task
    /// fire-and-forget sin await), terminaba re-lanzada por el finalizer como
    /// UnobservedTaskException y tumbaba la aplicación completa. Ahora TODA la cadena se
    /// inicia dentro de un único Dispatcher.InvokeAsync, para que sus `await` reanuden en
    /// el hilo de UI vía el SynchronizationContext del Dispatcher — mismo criterio que
    /// OnAttendancePunchReceived.</summary>
    private void OnRemoteSyncRequested(object? sender, RemoteSyncRequest request)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            AppendLog("📥 Solicitud de sincronización remota recibida desde el Dashboard" +
                (string.IsNullOrWhiteSpace(request.RequestedByEmail) ? "" : $" ({request.RequestedByEmail})") + "…");

            // Fire-and-forget deliberado: no se puede "esperar" aquí sin bloquear el hilo de
            // UI hasta que termine de conectar/descargar/sincronizar — pero sí debe
            // INICIARSE dentro de este InvokeAsync (ver comentario del método).
            _ = ProcessRemoteSyncRequestAsync(request);
        });
    }

    /// <summary>Procesa una solicitud remota reutilizando exactamente los mismos pasos que
    /// "Conectar" + "Descargar asistencias" harían a mano (nunca duplica esa lógica — ver
    /// <see cref="ConnectAsync"/>/<see cref="DownloadAttendanceCoreAsync"/>), y reporta el
    /// resultado de vuelta con <see cref="RemoteSyncRequestCoordinator.CompleteAsync"/> —
    /// así el Dashboard pasa de "Sincronizando…" a "Completado"/"Error" con un mensaje
    /// claro en cualquiera de los puntos donde puede fallar.
    ///
    /// El try/catch general es defensivo, no solo para el bug de hilos ya corregido en
    /// <see cref="OnRemoteSyncRequested"/>: este método se invoca fire-and-forget (nadie
    /// espera el Task ni observa su excepción), así que CUALQUIER excepción no atrapada
    /// aquí dentro —de cualquier causa futura, no solo la de hilos— se volvería una
    /// UnobservedTaskException que tumba la app entera en el finalizer. Mejor reportarla
    /// como solicitud fallida y seguir funcionando.</summary>
    private async Task ProcessRemoteSyncRequestAsync(RemoteSyncRequest request)
    {
        try
        {
            if (SelectedDevice is null)
            {
                AppendLog("⚠️ Solicitud remota rechazada: no hay ningún dispositivo seleccionado en esta PC.");
                await _remoteSyncCoordinator.CompleteAsync(
                    request.Id, success: false, "No hay ningún dispositivo seleccionado en esta PC.", CancellationToken.None);
                return;
            }

            if (!IsConnected)
            {
                // Reutiliza el mismo comando que el botón "Conectar" — incluye su propio
                // guardia de reentrancia (_isConnecting) y reactiva el auto-reconnect si
                // estaba suspendido por un "Desconectar" manual previo.
                await ConnectAsync();
            }

            if (!IsConnected)
            {
                AppendLog("⚠️ Solicitud remota fallida: no se pudo conectar con el reloj checador.");
                await _remoteSyncCoordinator.CompleteAsync(
                    request.Id, success: false, "No se pudo conectar con el reloj checador desde esta PC.", CancellationToken.None);
                return;
            }

            var (success, error, totalRead, savedCount) = await DownloadAttendanceCoreAsync();
            if (!success)
            {
                AppendLog($"⚠️ Solicitud remota fallida: no se pudo descargar del dispositivo ({error}).");
                await _remoteSyncCoordinator.CompleteAsync(
                    request.Id, success: false, $"No se pudo descargar del dispositivo: {error}", CancellationToken.None);
                return;
            }

            var pushOk = await _syncService.TriggerSyncNowAsync();
            if (pushOk)
            {
                var summary = $"{savedCount} marcación(es) nueva(s) de {totalRead} leída(s) del dispositivo, sincronizada(s) con la nube.";
                AppendLog($"✅ Solicitud remota completada: {summary}");
                await _remoteSyncCoordinator.CompleteAsync(request.Id, success: true, summary, CancellationToken.None);
            }
            else
            {
                const string message = "Se descargaron las marcaciones del dispositivo, pero falló la subida a la nube. Se reintentará solo en el siguiente ciclo.";
                AppendLog($"⚠️ Solicitud remota: {message}");
                await _remoteSyncCoordinator.CompleteAsync(request.Id, success: false, message, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al procesar una solicitud de sincronización remota (RequestId={RequestId})", request.Id);
            AppendLog($"⚠️ Solicitud remota fallida por un error inesperado: {ex.Message}");
            try
            {
                await _remoteSyncCoordinator.CompleteAsync(
                    request.Id, success: false, $"Error inesperado en la PC: {ex.Message}", CancellationToken.None);
            }
            catch (Exception completeEx)
            {
                // Si hasta reportar el fallo falla (p. ej. sin internet en ese instante), no
                // hay nada más que hacer aquí — se registra y se deja así; el Dashboard
                // seguirá mostrando "Sincronizando…" hasta que expire por su cuenta.
                Log.Warning(completeEx, "No se pudo reportar el fallo de la solicitud remota {RequestId} de vuelta a Supabase", request.Id);
            }
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

    /// <summary>Botón "Enviar empleados al reloj" — pedido explícito del usuario tras la
    /// importación masiva de 54 empleados ("agrega un botón para mandar esta información
    /// al reloj checador"). Escribe Nombre+PIN en la memoria del dispositivo vía
    /// SSR_SetUserInfo (<see cref="IAttendanceDeviceAdapter.CreateOrUpdateUserAsync"/> —
    /// ya existía en el adaptador desde antes pero nunca estuvo conectado a ningún botón)
    /// para cada empleado activo de la sucursal del dispositivo que todavía no tiene
    /// vínculo (<see cref="EmployeeDeviceMapping"/>) con él. Solo prepara el PIN para que
    /// la persona pueda enrolar su huella en el reloj — nunca sube huellas, eso sigue
    /// siendo un paso físico en el dispositivo.
    ///
    /// El PIN se asigna en automático (nunca el "Number" del negocio, p. ej. "EMP-001":
    /// el teclado del reloj es numérico y ese formato lo rechazaría) — decisión confirmada
    /// con el usuario. Para no chocar con usuarios que ya existan físicamente en el reloj
    /// desde antes de que existiera este vínculo local, primero se descarga la lista real
    /// del dispositivo (<see cref="IAttendanceDeviceAdapter.DownloadUsersAsync"/>) y se
    /// evita cualquier PIN que ya esté ocupado ahí, además de los que ya están en
    /// <see cref="EmployeeDeviceMapping"/> localmente.</summary>
    [RelayCommand]
    private async Task SendEmployeesToDeviceAsync()
    {
        var device = SelectedDevice;
        if (device is null)
        {
            AppendLog("⚠️ No se puede enviar: selecciona primero un dispositivo.");
            return;
        }

        if (!IsConnected)
        {
            AppendLog("⚠️ No se puede enviar: conecta primero con el dispositivo.");
            return;
        }

        if (_isSendingEmployees)
        {
            return;
        }

        _isSendingEmployees = true;
        try
        {
            var allEmployees = await _employeeRepository.ListAsync();
            var pending = allEmployees
                .Where(e => e.BranchId == device.BranchId && e.Status == EmploymentStatus.Active)
                .OrderBy(e => e.Number.Value)
                .ToList();

            if (pending.Count == 0)
            {
                AppendLog("No hay empleados activos en la sucursal de este dispositivo.");
                return;
            }

            var allMappings = await _mappingRepository.ListAsync();
            var deviceMappings = allMappings.Where(m => m.DeviceId == device.Id).ToList();
            var alreadyLinkedEmployeeIds = deviceMappings.Select(m => m.EmployeeId).ToHashSet();

            var toSend = pending.Where(e => !alreadyLinkedEmployeeIds.Contains(e.Id)).ToList();
            if (toSend.Count == 0)
            {
                AppendLog("Todos los empleados activos de esta sucursal ya están vinculados a este dispositivo.");
                return;
            }

            // PINs ocupados: los que ya están vinculados localmente + los que ya existan
            // físicamente en el reloj (por ejemplo, gente enrolada a mano antes de que
            // existiera este botón) — nunca se asume que el reloj está "limpio".
            var usedPins = new HashSet<int>();
            foreach (var mapping in deviceMappings)
            {
                if (int.TryParse(mapping.DeviceUserPin, out var pinFromMapping))
                {
                    usedPins.Add(pinFromMapping);
                }
            }

            var deviceUsersResult = await _deviceAdapter.DownloadUsersAsync();
            if (deviceUsersResult.IsSuccess)
            {
                foreach (var deviceUser in deviceUsersResult.Value)
                {
                    if (int.TryParse(deviceUser.DeviceUserPin, out var pinFromDevice))
                    {
                        usedPins.Add(pinFromDevice);
                    }
                }
            }
            else
            {
                AppendLog($"⚠️ No se pudo leer la lista actual de usuarios del reloj ({deviceUsersResult.Error.Message}); " +
                          "se continúa solo con los PINs ya vinculados localmente.");
            }

            AppendLog($"📤 Enviando {toSend.Count} empleado(s) nuevo(s) al reloj...");

            var nextPin = 1;
            var sentCount = 0;
            var failedNames = new List<string>();

            foreach (var employee in toSend)
            {
                while (usedPins.Contains(nextPin))
                {
                    nextPin++;
                }

                var pin = nextPin.ToString();
                var record = new DeviceUserRecord(pin, employee.FullName, PrivilegeLevel: 0, IsEnabled: true);
                var result = await _deviceAdapter.CreateOrUpdateUserAsync(record);

                if (result.IsFailure)
                {
                    failedNames.Add($"{employee.FullName} ({result.Error.Message})");
                    continue;
                }

                usedPins.Add(nextPin);
                var mapping = EmployeeDeviceMapping.Create(employee.Id, device.Id, pin);
                await _mappingRepository.AddAsync(mapping);
                sentCount++;
            }

            await _unitOfWork.SaveChangesAsync();

            AppendLog($"✅ Enviado(s) {sentCount} de {toSend.Count} empleado(s) nuevo(s) al reloj (PIN asignado en automático). " +
                      "Falta enrolar su huella físicamente en el dispositivo.");
            if (failedNames.Count > 0)
            {
                AppendLog($"⚠️ No se pudo enviar a: {string.Join(", ", failedNames)}.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al enviar empleados al dispositivo {DeviceId}", device.Id);
            AppendLog($"⚠️ Error inesperado al enviar empleados al reloj: {ex.Message}");
        }
        finally
        {
            _isSendingEmployees = false;
        }
    }

    private void AppendLog(string message) =>
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} — {message}");

    private void RefreshStatusMessage() =>
        StatusMessage = Devices.Count == 0
            ? "Aún no hay dispositivos registrados."
            : $"{Devices.Count} dispositivo(s) registrado(s) en la base local.";
}
