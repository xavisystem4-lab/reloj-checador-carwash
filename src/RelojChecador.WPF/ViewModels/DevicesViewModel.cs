using System.Collections.ObjectModel;
using System.Linq;
using System.Net.NetworkInformation;
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

/// <summary>Una fila de la lista "Usuarios del reloj" (DeviceUsersDialog) — envuelve
/// <see cref="DeviceUserRecord"/> (inmutable) agregando <see cref="IsSelected"/> para el
/// checkbox de selección masiva, que sí necesita notificar cambios a la UI.</summary>
public sealed partial class DeviceUserRow : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected;

    public string DeviceUserPin { get; }
    public string Name { get; }
    public int PrivilegeLevel { get; }
    public bool IsEnabled { get; }

    /// <summary>Etiqueta best-effort — igual que el resto del mapeo de códigos del SDK de
    /// ZKTeco en este proyecto (ver ZKTecoDeviceAdapter.MapVerifyMode), la convención más
    /// citada usa 0 = usuario común y valores mayores (14 = Super Admin es el más común)
    /// para privilegios especiales; no confirmada contra hardware real.</summary>
    public string PrivilegeLabel => PrivilegeLevel == 0 ? "Usuario" : "Administrador";
    public string EnabledLabel => IsEnabled ? "Sí" : "No";

    public DeviceUserRow(DeviceUserRecord record)
    {
        DeviceUserPin = record.DeviceUserPin;
        Name = record.Name;
        PrivilegeLevel = record.PrivilegeLevel;
        IsEnabled = record.IsEnabled;
    }
}

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
    private readonly IDeviceCredentialStore _credentialStore;

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
    //
    // IMPORTANTE (corregido — reportado por el usuario: la app se congelaba con "No
    // responde" al abrir o al editar un dispositivo): este timer y _autoDownloadTimer se
    // CREAN aquí pero YA NO se arrancan en el constructor. Antes sí arrancaban de
    // inmediato, y como MainWindow recibe DevicesViewModel directo en su constructor, eso
    // significaba un intento de conexión automático contra el reloj real en cuanto se abría
    // la ventana principal — antes de que el usuario tocara nada, y antes de que
    // ConnectAsync() tuviera ningún tiempo de espera real. Ahora ambos timers arrancan
    // recién dentro de ConnectAsync() (el usuario presionó "Conectar" al menos una vez) y
    // se detienen POR COMPLETO en DisconnectAsync()/OnSelectedDeviceChanged()/Dispose() —
    // nunca vuelven a correr solos hasta el siguiente "Conectar" explícito.
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
    //
    // Igual que _autoReconnectTimer: se crea aquí pero arranca solo dentro de ConnectAsync().
    private readonly DispatcherTimer _autoDownloadTimer;
    private bool _isDownloading;

    // Cancelación real de la conexión en curso (reportado por el usuario: al cambiar de
    // dispositivo, editar uno, o perder la red, un intento de Connect_Net colgado seguía
    // vivo de fondo contra el reloj anterior). Se crea uno nuevo en cada ConnectAsync() —
    // cancelando y desechando el anterior primero si quedaba alguno vivo — y se cancela en
    // DisconnectAsync(), OnSelectedDeviceChanged(), OnNetworkAvailabilityChanged() (red
    // perdida) y Dispose(). No puede abortar una llamada COM ya en marcha a media ejecución
    // (ver comentario de RunWithTimeoutAsync en ZKTecoDeviceAdapter), pero sí evita que se
    // sigan disparando intentos nuevos y deja que el que esté esperando el resultado
    // (RunWithTimeoutAsync ya tiene su propio tiempo de espera) suelte la UI de inmediato.
    private CancellationTokenSource? _connectionCts;

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

    // Todos los dispositivos de la base local, incluidos los deshabilitados (ver
    // DeleteDeviceAsync — "eliminar" es baja lógica, nunca borra el registro ni su
    // historial). Devices (la colección que se bindea a la UI) es un subconjunto filtrado de
    // esta lista — mismo criterio que EmployeesViewModel._allRows/ShowTerminatedEmployees.
    private List<Device> _allDevices = [];

    /// <summary>Por defecto los dispositivos deshabilitados se ocultan de la lista — este
    /// toggle los vuelve a mostrar sin necesidad de recargar la base de datos otra vez
    /// (filtra en memoria sobre _allDevices, ver ApplyDeviceVisibilityFilter).</summary>
    [ObservableProperty]
    private bool _showDisabledDevices;

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
        RemoteSyncRequestCoordinator remoteSyncCoordinator,
        IDeviceCredentialStore credentialStore)
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
        _credentialStore = credentialStore;

        // El adaptador es Singleton (una sola instancia para toda la app — ver comentario de
        // clase) pero este ViewModel es Scoped (una instancia por ventana); por eso hay que
        // desuscribirse explícitamente en Dispose(), o cada ventana nueva dejaría un
        // manejador colgado apuntando a un ViewModel ya descartado.
        _deviceAdapter.AttendancePunchReceived += OnAttendancePunchReceived;

        // Se crean SIN arrancar — ver el comentario junto a los campos. Arrancan recién
        // dentro de ConnectAsync(), cuando el usuario presiona "Conectar" por primera vez.
        _autoReconnectTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _autoReconnectTimer.Tick += async (_, _) => await TryAutoReconnectAsync();

        _autoDownloadTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoDownloadTimer.Tick += async (_, _) => await TryAutoDownloadAsync();

        // Igual que _deviceAdapter.AttendancePunchReceived: se dispara en el hilo del
        // RemoteSyncRequestPollingService, no en el de UI — el handler hace su propio
        // marshaling (ver OnRemoteSyncRequested). RemoteSyncRequestCoordinator es
        // Singleton, así que hay que desuscribirse en Dispose() por la misma razón que el
        // adaptador: este ViewModel es Scoped, un ViewModel nuevo por ventana no debe
        // dejar un manejador colgado apuntando a uno ya descartado.
        _remoteSyncCoordinator.SyncRequested += OnRemoteSyncRequested;

        // Red perdida (requisito explícito del usuario: "si se pierde la conexión a
        // internet o a la red local, deben detenerse inmediatamente todos los procesos de
        // conexión y sincronización"). Se dispara en un hilo de .NET distinto al de UI, así
        // que el handler hace su propio marshaling (igual que AttendancePunchReceived).
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
    }

    public void Dispose()
    {
        _deviceAdapter.AttendancePunchReceived -= OnAttendancePunchReceived;
        _remoteSyncCoordinator.SyncRequested -= OnRemoteSyncRequested;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _autoReconnectTimer.Stop();
        _autoDownloadTimer.Stop();
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
    }

    /// <summary>Se dispara en cuanto Windows detecta que se perdió (o volvió) la red — no
    /// depende de esperar a que fallen 2-3 descargas seguidas (~30s, ver
    /// DownloadAttendanceCoreAsync) para reaccionar. Al perderse: cancela cualquier
    /// operación en curso, detiene los timers automáticos por completo y marca desconectado
    /// de inmediato. Al recuperarse: NO reconecta por sí solo aquí (evita reconectar en
    /// medio de un evento de red que puede repetirse varias veces en un segundo) — solo
    /// vuelve a arrancar _autoReconnectTimer si el usuario no había desconectado a mano, y
    /// el timer se encarga de reintentar en su siguiente ciclo (máximo 15s después),
    /// respetando el mismo tiempo de espera acotado que cualquier otro intento de conexión.</summary>
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            if (!e.IsAvailable)
            {
                if (!IsConnected && !_autoReconnectTimer.IsEnabled)
                {
                    return; // Nada que detener — no había ninguna conexión/timer activo.
                }

                AppendLog("🔌 Se perdió la red — deteniendo conexión y sincronización con el dispositivo.");
                _connectionCts?.Cancel();
                _autoReconnectTimer.Stop();
                _autoDownloadTimer.Stop();
                IsConnected = false;
                IsMonitoringRealTime = false;
                return;
            }

            // Red recuperada: si había un dispositivo seleccionado y el usuario no lo había
            // desconectado a mano, se reactiva el auto-reconnect para que retome solo, sin
            // forzar un intento inmediato aquí mismo (evita una ráfaga de intentos si la red
            // parpadea varias veces seguidas al reconectar).
            if (SelectedDevice is not null && !_autoReconnectSuspended && !_autoReconnectTimer.IsEnabled)
            {
                AppendLog("🔌 Red recuperada — se reintentará conectar en el siguiente ciclo.");
                _autoReconnectTimer.Start();
                _autoDownloadTimer.Start();
            }
        });
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
    /// algo salió mal, igual que el resto de los timers automáticos de este ViewModel.
    ///
    /// CAUSA REAL de "Desconectado (hace Xh)" persistente en el Dashboard pese a que la
    /// app mostraba "Conectado" (reportado por el usuario, confirmado directamente contra
    /// Supabase: last_communication_at_utc congelado desde hacía horas en TODAS las
    /// tablas): Device.LastCommunicationAtUtc solo se tocaba en dos eventos puntuales —
    /// un Conectar/Desconectar, o una marcación real (ver PersistAttendanceAsync). Si el
    /// dispositivo se queda conectado en vivo pero nadie poncha durante varias horas
    /// (de madrugada, por ejemplo), ese campo nunca se refresca — aunque el reloj siga
    /// perfectamente conectado y "Conectado (hace 0s)" en la barra superior sea real (ese
    /// texto es el ciclo de sincronización con Supabase, que sí corre cada 10s — ver
    /// UpdateViewModel.CloudSyncShortStatus, NO el estado del dispositivo físico: son dos
    /// cosas distintas). Una descarga automática EXITOSA (aunque traiga 0 marcaciones
    /// nuevas) ya es prueba real de que el dispositivo respondió ahora mismo, así que
    /// sirve como "heartbeat": refresca LastCommunicationAtUtc y lo empuja a Supabase de
    /// inmediato (TryPersistCommunicationResultAsync ya hace ambas cosas).</summary>
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

        await TryPersistCommunicationResultAsync(succeeded: true);

        if (savedCount > 0)
        {
            AppendLog($"🔄 Descarga automática: {savedCount} marcación(es) nueva(s) de {totalRead} leída(s).");
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
            _allDevices = (await _deviceRepository.ListAsync()).ToList();
            ApplyDeviceVisibilityFilter();

            RefreshStatusMessage();
            SelectedDevice = Devices.FirstOrDefault();

            // Corregido — reportado por el usuario: la app se congelaba con "No responde" al
            // abrir, porque aquí mismo se disparaba un intento de conexión automático contra
            // el reloj real antes de que la ventana terminara de mostrarse. Ya NO se conecta
            // solo al cargar: solo se listan y se selecciona el primero para que el panel de
            // diagnóstico tenga algo que mostrar — la conexión real empieza únicamente cuando
            // el usuario presiona "Conectar" (ver ConnectAsync).
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de dispositivos.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
    }

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync() => await _branchRepository.ListAsync();

    /// <param name="communicationKey">Clave de comunicación del dispositivo (algunos equipos
    /// EBKN/ZK la exigen para aceptar la conexión — ver ZKTecoDeviceAdapter.ConnectAsync).
    /// Vacía/null = sin clave, comportamiento de siempre. Nunca se guarda en la base de
    /// datos local: se guarda en Windows Credential Manager (ver IDeviceCredentialStore) y
    /// solo se persiste en el dispositivo la REFERENCIA hacia esa entrada.</param>
    public async Task<string?> CreateDeviceAsync(
        string name, string brand, string model, string ipAddress, int tcpPort, Guid branchId,
        string timeZoneId, string? serialNumber, string? macAddress, string? communicationKey = null)
    {
        try
        {
            var device = Device.Register(name, brand, model, ipAddress, tcpPort, branchId, timeZoneId, serialNumber, macAddress);
            await _deviceRepository.AddAsync(device);
            await _unitOfWork.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(communicationKey))
            {
                await SaveCommunicationKeyAsync(device, communicationKey);
            }

            // Un dispositivo recién creado nunca nace deshabilitado (Status inicial =
            // Unknown), así que agregarlo directo a ambas listas es seguro sin tener que
            // recalcular todo el filtro — mismo atajo que ya hacía este método antes.
            _allDevices.Add(device);
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
    /// <param name="communicationKey">Igual que en CreateDeviceAsync — vacío/null significa
    /// "no cambiar la clave ya guardada" (nunca se muestra la clave existente de vuelta en
    /// la pantalla de edición, así que dejar el campo en blanco no puede interpretarse como
    /// "quitar la clave"). Solo se guarda/reemplaza si el usuario escribió algo nuevo.</param>
    /// <param name="deleteCommunicationKey">Pedido explícito del usuario en "Editar
    /// dispositivo": una casilla separada para BORRAR la clave ya guardada, distinta de
    /// dejar el campo en blanco (que significa "no tocarla"). Si ambos vienen con valor
    /// (raro — la UI no debería permitirlo, ver EditDeviceDialog), guardar la nueva gana:
    /// no tendría sentido borrar y reemplazar en la misma operación.</param>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateDeviceAsync(
        Guid deviceId, string name, string brand, string model, string ipAddress, int tcpPort,
        Guid branchId, string timeZoneId, string? serialNumber, string? macAddress,
        string? communicationKey = null, bool deleteCommunicationKey = false)
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

            if (!string.IsNullOrWhiteSpace(communicationKey))
            {
                await SaveCommunicationKeyAsync(device, communicationKey);
            }
            else if (deleteCommunicationKey)
            {
                await DeleteCommunicationKeyAsync(device);
            }

            await _unitOfWork.SaveChangesAsync();
            await _syncService.TriggerSyncNowAsync();

            _allDevices = (await _deviceRepository.ListAsync()).ToList();
            ApplyDeviceVisibilityFilter();

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

    /// <summary>Guarda la clave en Windows Credential Manager y asigna la referencia al
    /// dispositivo. Usa el Id del dispositivo como referencia estable — sobrescribir la
    /// clave de un dispositivo ya existente reutiliza la misma entrada en vez de dejar
    /// huérfanas las anteriores. Un fallo al guardar la clave nunca debe impedir guardar el
    /// resto de los datos del dispositivo (IP/puerto/etc.) — solo se registra en el log.</summary>
    private async Task SaveCommunicationKeyAsync(Device device, string communicationKey)
    {
        var reference = $"device-{device.Id}";
        var saveResult = await _credentialStore.SaveAsync(reference, communicationKey);
        if (saveResult.IsFailure)
        {
            Log.Warning("No se pudo guardar la clave de comunicación del dispositivo {DeviceId}: {Error}", device.Id, saveResult.Error.Message);
            return;
        }

        device.AssignCredentialReference(reference);
    }

    /// <summary>Borra la clave guardada de Windows Credential Manager y limpia la referencia
    /// en el dispositivo — pedido explícito del usuario: "Editar dispositivo" necesitaba una
    /// forma de QUITAR la clave, no solo reemplazarla (dejar el campo en blanco al editar
    /// nunca borra nada, ver UpdateDeviceAsync). Si el dispositivo no tenía ninguna clave
    /// guardada (CredentialReference nulo), no hay nada que hacer.</summary>
    private async Task DeleteCommunicationKeyAsync(Device device)
    {
        if (string.IsNullOrEmpty(device.CredentialReference))
        {
            return;
        }

        var deleteResult = await _credentialStore.DeleteAsync(device.CredentialReference);
        if (deleteResult.IsFailure)
        {
            Log.Warning("No se pudo borrar la clave de comunicación del dispositivo {DeviceId}: {Error}", device.Id, deleteResult.Error.Message);
            return;
        }

        device.ClearCredentialReference();
    }

    /// <summary>Para que "Editar dispositivo" pueda mostrar "Ya hay una clave guardada" en vez
    /// de dejar la pantalla ambigua sobre si existe una o no.</summary>
    public bool HasCommunicationKey(Device device) => !string.IsNullOrEmpty(device.CredentialReference);

    /// <summary>Reconstruye <see cref="Devices"/> a partir de <see cref="_allDevices"/>
    /// aplicando el filtro "Mostrar dispositivos deshabilitados" — mismo criterio que
    /// EmployeesViewModel.ApplyVisibilityFilter/ShowTerminatedEmployees: "eliminar" un
    /// dispositivo (ver DeleteDeviceAsync) es una baja lógica, nunca borra su historial, así
    /// que por defecto se oculta de la lista pero puede volver a mostrarse sin recargar la
    /// base de datos.</summary>
    private void ApplyDeviceVisibilityFilter()
    {
        IEnumerable<Device> visible = ShowDisabledDevices
            ? _allDevices
            : _allDevices.Where(d => d.Status != DeviceStatus.Disabled);

        Devices.Clear();
        foreach (var device in visible)
        {
            Devices.Add(device);
        }
    }

    partial void OnShowDisabledDevicesChanged(bool value) => ApplyDeviceVisibilityFilter();

    /// <summary>Da de baja un dispositivo — baja lógica, igual criterio que
    /// EmployeesViewModel.DeleteEmployeeAsync: el registro se conserva (con todo su
    /// historial de asistencias y vínculos a empleados), solo se marca Disabled y se oculta
    /// de la lista por defecto. Nunca se borra de verdad: las marcaciones ya descargadas de
    /// ese dispositivo seguirían referenciando su Id — un borrado físico las dejaría
    /// huérfanas o rompería el reporte histórico.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> DeleteDeviceAsync(Guid deviceId)
    {
        try
        {
            var device = await _deviceRepository.GetByIdAsync(deviceId);
            if (device is null)
            {
                return "No se encontró el dispositivo — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            // Si el dispositivo que se está dando de baja es el seleccionado y sigue
            // conectado/monitoreando en tiempo real, hay que cortar esa conexión primero —
            // mismo motivo que OnSelectedDeviceChanged: no debe quedar nada corriendo de
            // fondo contra un dispositivo que la app ya no debería tocar.
            if (SelectedDevice?.Id == deviceId && IsConnected)
            {
                _connectionCts?.Cancel();
                _autoReconnectTimer.Stop();
                _autoDownloadTimer.Stop();
                await _deviceAdapter.StopRealTimeMonitoringAsync();
                await _deviceAdapter.DisconnectAsync();
                IsConnected = false;
                IsMonitoringRealTime = false;
            }

            device.Disable();
            await _unitOfWork.SaveChangesAsync();

            _allDevices = (await _deviceRepository.ListAsync()).ToList();
            ApplyDeviceVisibilityFilter();

            RefreshStatusMessage();
            SelectedDevice = Devices.FirstOrDefault();
            return null;
        }
        catch (DbUpdateException ex)
        {
            Log.Warning(ex, "No se pudo dar de baja el dispositivo (DeviceId={DeviceId})", deviceId);
            return "No se pudo dar de baja el dispositivo. Verifica los datos e intenta de nuevo.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al dar de baja el dispositivo {DeviceId}", deviceId);
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
        //
        // Requisito explícito del usuario: no debe quedar ninguna conexión o proceso del
        // dispositivo ANTERIOR corriendo al cambiar de selección (esto también dispara al
        // editar un dispositivo — UpdateDeviceAsync reasigna SelectedDevice al terminar de
        // guardar). Se cancela cualquier intento en curso y se detienen los timers por
        // completo — el dispositivo recién seleccionado NO se conecta solo: hace falta que
        // el usuario presione "Conectar" explícitamente, igual que al abrir la app.
        _connectionCts?.Cancel();
        _autoReconnectTimer.Stop();
        _autoDownloadTimer.Stop();

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

        // El dispositivo recién seleccionado empieza sin suspensión — así que si el usuario
        // presiona "Conectar" sobre él, el auto-reconnect seguirá intentando por su cuenta
        // después, sin quedar bloqueado por un "Desconectar" hecho sobre el anterior.
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

        // Un token nuevo por cada intento — si quedaba uno anterior vivo (no debería, pero
        // por si un "Conectar" se disparó dos veces seguidas) se cancela y se desecha antes,
        // nunca se dejan dos intentos corriendo a la vez contra el mismo dispositivo.
        _connectionCts?.Cancel();
        _connectionCts?.Dispose();
        _connectionCts = new CancellationTokenSource();
        var token = _connectionCts.Token;

        // Los timers automáticos arrancan aquí, no en el constructor — ver el comentario
        // junto a los campos. Arrancan con el primer "Conectar" (a mano o disparado por el
        // propio auto-reconnect una vez que ya arrancó antes) y de ahí en adelante siguen
        // el mismo comportamiento de siempre (reintentar cada 15s / descargar cada 10s)
        // hasta que el usuario presione "Desconectar" o cambie de dispositivo.
        _autoReconnectTimer.Start();
        _autoDownloadTimer.Start();

        try
        {
            // Si el dispositivo tiene una clave de comunicación guardada (ver
            // SaveCommunicationKeyAsync), se recupera aquí y se manda al adaptador —
            // algunos equipos (reportado por el usuario, un EBKN/EN-260 real) no aceptan
            // Connect_Net sin ella. Sin clave guardada (el caso normal), se conecta igual
            // que siempre.
            string? communicationKey = SelectedDevice.CredentialReference is { } reference
                ? await _credentialStore.GetAsync(reference, token)
                : null;
            var connectionInfo = new DeviceConnectionInfo(SelectedDevice.IpAddress, SelectedDevice.TcpPort, communicationKey);
            var result = await _deviceAdapter.ConnectAsync(connectionInfo, token);

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
            // Cada conexión nueva empieza su propia racha de fallos de lectura desde cero
            // — ver el comentario de DownloadAttendanceCoreAsync sobre por qué se exige
            // más de un fallo seguido antes de dar por muerta la conexión.
            _consecutiveDownloadFailures = 0;
            AppendLog("Comunicación completa establecida con el dispositivo.");
            await TryPersistCommunicationResultAsync(succeeded: true);

            // Al conectar, arranca solo el monitoreo en vivo — no hay que presionar nada
            // aparte para que una marcación aparezca en la lista casi al instante.
            var monitorResult = await _deviceAdapter.StartRealTimeMonitoringAsync(token);
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

        // Requisito explícito del usuario: "Desconectar" debe detener POR COMPLETO
        // conexiones, tareas en segundo plano, reintentos, temporizadores y sincronización
        // — no solo dejar de reconectar. Se cancela cualquier intento en curso (aunque no
        // pueda abortar una llamada COM ya en marcha, ver RunWithTimeoutAsync, sí evita que
        // la UI siga esperándola) y se detienen los timers por completo, no solo se
        // suspenden — vuelven a arrancar recién con el siguiente "Conectar".
        _connectionCts?.Cancel();
        _autoReconnectTimer.Stop();
        _autoDownloadTimer.Stop();

        await _deviceAdapter.StopRealTimeMonitoringAsync();
        IsMonitoringRealTime = false;
        await _deviceAdapter.DisconnectAsync();
        IsConnected = false;

        // Antes ProtocolResult/AuthResult se quedaban en "✅ Protocolo reconocido"/"✅
        // Autenticación correcta" para siempre tras "Desconectar" (solo OnSelectedDeviceChanged
        // los reiniciaba, y eso exige cambiar de dispositivo) — quien mirara esta pantalla veía
        // puros checkmarks verdes y creía que seguía conectado, aunque IsConnected ya fuera
        // false y el Dashboard (basado en ese mismo estado) ya mostrara "Desconectado" — no
        // eran dos sistemas en desacuerdo, era la UI local mostrando un resultado viejo.
        ProtocolResult = "No verificado";
        AuthResult = "No verificado";
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
    /// AttendanceRecords entre sí.
    ///
    /// CAUSA REAL reportada por el usuario ("dice Conectado pero no trae las últimas
    /// checadas ni al presionar 'Descargar asistencias' a mano"): un fallo real de lectura
    /// (el reloj dejó de responder de verdad — cambio de IP por DHCP, se reinició, perdió
    /// red, etc.) nunca tocaba <see cref="IsConnected"/>. La UI se quedaba mostrando
    /// "Conectado" para siempre (nadie lo volvía a evaluar), y como TryAutoReconnectAsync
    /// solo actúa cuando IsConnected es false, el auto-reconnect (cada 15s) jamás
    /// intentaba una reconexión real — quedaba en un punto muerto silencioso hasta que
    /// alguien presionara "Desconectar" y "Conectar" a mano.
    ///
    /// REGRESIÓN real de esa misma corrección (v1.19.0, reportada por el usuario: "en la
    /// 1.17.2 sí funcionaba y ahorita ya no... el dispositivo está prendido... hay
    /// internet pero no comunica"): marcar desconectado ante CUALQUIER fallo, incluido uno
    /// pasajero (un timeout puntual del SDK de ZKTeco, por ejemplo), cortaba una conexión
    /// que en realidad seguía sana — antes, ese mismo fallo pasajero se ignoraba solo y el
    /// siguiente ciclo de 10s reintentaba sobre la MISMA conexión ya abierta, sin
    /// problema. Forzar una reconexión completa desde cero en cada fallo resultó ser
    /// menos confiable que simplemente reintentar, porque el handshake de reconexión (los
    /// 5 niveles de diagnóstico) es más propenso a fallar que una lectura sobre una
    /// conexión ya establecida. _consecutiveDownloadFailures exige varios fallos SEGUIDOS
    /// (no uno solo) antes de dar por muerta la conexión — un fallo aislado se sigue
    /// ignorando igual que antes de v1.19.0; solo una racha sostenida (~30s) dispara la
    /// reconexión real.</summary>
    private const int MaxConsecutiveDownloadFailures = 3;
    private int _consecutiveDownloadFailures;

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
                _consecutiveDownloadFailures++;
                if (_consecutiveDownloadFailures >= MaxConsecutiveDownloadFailures)
                {
                    await MarkDisconnectedDueToFailureAsync(
                        $"{result.Error.Message} (tras {_consecutiveDownloadFailures} intentos fallidos seguidos)");
                }

                return (false, result.Error.Message, 0, 0);
            }

            _consecutiveDownloadFailures = 0;
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

    /// <summary>Corrige el punto muerto descrito en el comentario de
    /// <see cref="DownloadAttendanceCoreAsync"/>: si un intento de leer del dispositivo
    /// falla de verdad, se corta el monitoreo en tiempo real, se marca IsConnected=false
    /// (para que TryAutoReconnectAsync deje de considerarlo "ya conectado" y vuelva a
    /// intentarlo en el siguiente tick de 15s) y se deja constancia + se empuja el fallo a
    /// Supabase de inmediato. No hace nada si ya estaba marcado como desconectado (evita
    /// registrar el mismo fallo una y otra vez cada 10s mientras el auto-reconnect sigue
    /// sin lograrlo).</summary>
    private async Task MarkDisconnectedDueToFailureAsync(string reason)
    {
        if (!IsConnected)
        {
            return;
        }

        AppendLog($"⚠️ Se perdió la comunicación real con el dispositivo: {reason}");
        _ = _deviceAdapter.StopRealTimeMonitoringAsync();
        _ = _deviceAdapter.DisconnectAsync();
        IsMonitoringRealTime = false;
        IsConnected = false;

        // Mismo motivo que en DisconnectAsync: sin esto, ProtocolResult/AuthResult seguían
        // mostrando los checkmarks verdes de la última conexión exitosa horas atrás — la
        // única pista de que el reloj ya no respondía de verdad quedaba enterrada en la
        // Bitácora, nadie la mira a cada rato. Ahora el mismo panel de diagnóstico que
        // mostró "conectado" es el que avisa que dejó de estarlo.
        ProtocolResult = "No verificado";
        AuthResult = $"❌ Comunicación perdida — {reason}";

        await TryPersistCommunicationResultAsync(succeeded: false);
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

    /// <summary>Un empleado puntual que falló dentro de un envío que sí corrió — nombre +
    /// motivo legible (nunca un stack trace: eso solo va al log técnico, ver
    /// SendEmployeesToDeviceAsync).</summary>
    public sealed record SendEmployeeFailure(string EmployeeName, string Reason);

    /// <summary>Progreso en vivo de <see cref="SendEmployeesToDeviceAsync"/> — pensado para
    /// alimentar directo una barra de progreso/diálogo modal (ver
    /// SendEmployeesProgressDialog): <see cref="Processed"/>/<see cref="Total"/> para el
    /// porcentaje real, <see cref="CurrentEmployeeName"/> para "Enviando a Fulano...", y los
    /// tres contadores para mostrar éxito/fallo/omitido sin esperar al resumen final.</summary>
    public sealed record SendEmployeesProgress(
        int Processed, int Total, string CurrentEmployeeName, int Sent, int Failed, int Skipped);

    /// <summary>Resultado final de <see cref="SendEmployeesToDeviceAsync"/> — <see cref="Error"/>
    /// no nulo significa que ni siquiera se intentó enviar nada (sin dispositivo
    /// seleccionado, sin conexión activa, ya había un envío en curso); con <see cref="Error"/>
    /// nulo, revisa <see cref="Failed"/> para el detalle de quién falló y por qué, y
    /// <see cref="Cancelled"/> para saber si se detuvo a medias por cancelación del usuario
    /// (<see cref="Skipped"/> cuenta a quien ni siquiera se llegó a intentar en ese caso).
    /// <see cref="NothingToSendReason"/> explica un <see cref="Total"/> en 0 que NO es un
    /// error — p. ej. "no hay empleados activos en esa sucursal" vs. "todos ya estaban en
    /// el reloj" son motivos muy distintos y, sin este campo, el diálogo de progreso solo
    /// podía mostrar el ambiguo "Completado — 0 de 0" sin decir por qué (reportado por el
    /// usuario: "no sube la información").</summary>
    public sealed record SendEmployeesOutcome(
        int Sent, int Total, int Skipped, IReadOnlyList<SendEmployeeFailure> Failed, bool Cancelled, string? Error,
        string? NothingToSendReason = null)
    {
        public bool Success => Error is null;
    }

    /// <summary>"Enviar empleados al reloj" — pedido explícito del usuario tras la
    /// importación masiva de 54 empleados ("agrega un botón para mandar esta información
    /// al reloj checador"), y movido después de Dispositivos al módulo de Empleados (ver
    /// EmployeesView.xaml.cs, OnSendEmployeesToDeviceClick — orquesta Conectar → esto →
    /// Desconectar sobre esta misma instancia de DevicesViewModel, compartida entre
    /// pestañas dentro de una misma ventana). Escribe Nombre+PIN en la memoria del
    /// dispositivo vía SSR_SetUserInfo (<see cref="IAttendanceDeviceAdapter.CreateOrUpdateUserAsync"/>).
    /// Solo prepara el PIN para que la persona pueda enrolar su huella en el reloj — nunca
    /// sube huellas, eso sigue siendo un paso físico en el dispositivo.
    ///
    /// Este método es el único punto que de verdad reconcilia la base local con la memoria
    /// FÍSICA del reloj (<see cref="IAttendanceDeviceAdapter.DownloadUsersAsync"/>), sin
    /// importar de dónde salió el vínculo local (<see cref="EmployeeDeviceMapping"/>):
    /// <list type="bullet">
    /// <item>quien todavía no tiene vínculo → se le asigna un PIN nuevo en automático (nunca
    /// el "Number" del negocio, p. ej. "EMP-001": el teclado del reloj es numérico y ese
    /// formato lo rechazaría) y se crea el vínculo;</item>
    /// <item>quien YA tiene vínculo local pero su PIN todavía no aparece en la lista real del
    /// reloj — pedido explícito del usuario al agregar la columna Pin a "Reemplazar
    /// catálogo" ("de esa manera suba la información al reloj checador"): ese flujo solo
    /// crea el vínculo local, nunca toca el dispositivo por sí mismo (podría no estar ni
    /// conectado en ese momento) — se sube con el PIN que ya tenía asignado, sin tocar el
    /// vínculo.</item>
    /// </list>
    /// A quien ya está vinculado Y ya aparece en el reloj no se le vuelve a enviar nada.
    ///
    /// Cada empleado se procesa AISLADO dentro de su propio try/catch (nunca se marca a
    /// nadie como sincronizado hasta tener la confirmación de éxito del reloj, y su vínculo
    /// se guarda de inmediato — <c>await _unitOfWork.SaveChangesAsync()</c> por empleado, no
    /// uno solo al final): si uno falla, el error queda registrado y el envío sigue con el
    /// siguiente; si <paramref name="cancellationToken"/> se cancela a medias, lo que ya se
    /// envió con éxito queda guardado (no se reintenta ni se duplica en el siguiente envío).
    /// <paramref name="progress"/> reporta cada paso para un diálogo modal — ver
    /// SendEmployeesProgress y EmployeesView.xaml.cs/SendEmployeesProgressDialog.</summary>
    public async Task<SendEmployeesOutcome> SendEmployeesToDeviceAsync(
        IProgress<SendEmployeesProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var device = SelectedDevice;
        if (device is null)
        {
            AppendLog("⚠️ No se puede enviar: selecciona primero un dispositivo.");
            return new SendEmployeesOutcome(0, 0, 0, [], false, "Selecciona primero un dispositivo.");
        }

        if (!IsConnected)
        {
            AppendLog("⚠️ No se puede enviar: conecta primero con el dispositivo.");
            return new SendEmployeesOutcome(0, 0, 0, [], false, "No hay conexión activa con el dispositivo.");
        }

        if (_isSendingEmployees)
        {
            return new SendEmployeesOutcome(0, 0, 0, [], false, "Ya hay un envío en curso.");
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
                // Motivo real más citado de este caso (reportado por el usuario: "no sube
                // la información" con la lista de empleados visiblemente llena detrás del
                // diálogo): la sucursal del dispositivo NO es la misma sucursal a la que
                // quedaron asignados esos empleados — p. ej. tras importar un catálogo cuyo
                // nombre de sucursal no coincidía exactamente con el existente y creó una
                // sucursal nueva por error (ver "Reemplazar catálogo"). Se nombra la
                // sucursal exacta del dispositivo para que sea fácil de verificar contra la
                // sucursal real de esos empleados en la pantalla de Empleados.
                var branchName = (await _branchRepository.GetByIdAsync(device.BranchId))?.Name ?? "(sin nombre)";
                var reason = allEmployees.Any(e => e.Status == EmploymentStatus.Active)
                    ? $"Este dispositivo está asignado a la sucursal \"{branchName}\", y no hay ningún empleado activo en ESA sucursal — revisa en Empleados a qué sucursal quedaron asignados (pudo crearse una sucursal distinta por un nombre que no coincidía exactamente al importar)."
                    : "No hay ningún empleado activo registrado todavía.";
                AppendLog($"No hay empleados activos en la sucursal de este dispositivo (\"{branchName}\").");
                return new SendEmployeesOutcome(0, 0, 0, [], false, null, reason);
            }

            var allMappings = await _mappingRepository.ListAsync();
            var deviceMappings = allMappings.Where(m => m.DeviceId == device.Id).ToList();
            var mappingByEmployeeId = deviceMappings.ToDictionary(m => m.EmployeeId, m => m);

            // PINs realmente en el reloj — determina tanto qué evitar al asignar en
            // automático como quién, aunque ya tenga vínculo local, todavía no llegó de
            // verdad al dispositivo (p. ej. un PIN cargado por "Reemplazar catálogo" antes
            // de conectar el reloj). Si la descarga falla, se asume que el reloj podría no
            // tener a nadie todavía — CreateOrUpdateUserAsync es "crear o actualizar", así
            // que reenviar a alguien que ya estaba no hace daño.
            var devicePins = new HashSet<string>();
            var deviceUsersResult = await _deviceAdapter.DownloadUsersAsync(cancellationToken);
            if (deviceUsersResult.IsSuccess)
            {
                foreach (var deviceUser in deviceUsersResult.Value)
                {
                    devicePins.Add(deviceUser.DeviceUserPin);
                }
            }
            else
            {
                AppendLog($"⚠️ No se pudo leer la lista actual de usuarios del reloj ({deviceUsersResult.Error.Message}); " +
                          "se continúa solo con los PINs ya vinculados localmente.");
            }

            var usedPins = new HashSet<int>();
            foreach (var mapping in deviceMappings)
            {
                if (int.TryParse(mapping.DeviceUserPin, out var pinFromMapping))
                {
                    usedPins.Add(pinFromMapping);
                }
            }
            foreach (var pin in devicePins)
            {
                if (int.TryParse(pin, out var pinFromDevice))
                {
                    usedPins.Add(pinFromDevice);
                }
            }

            var toAutoAssign = pending.Where(e => !mappingByEmployeeId.ContainsKey(e.Id)).ToList();
            var toPushExisting = pending
                .Where(e => mappingByEmployeeId.TryGetValue(e.Id, out var m) && !devicePins.Contains(m.DeviceUserPin))
                .ToList();
            var totalToSend = toAutoAssign.Count + toPushExisting.Count;

            if (totalToSend == 0)
            {
                AppendLog($"Todos los {pending.Count} empleado(s) activo(s) de esta sucursal ya están en el reloj.");
                return new SendEmployeesOutcome(0, 0, 0, [], false, null,
                    $"Los {pending.Count} empleado(s) activo(s) de esta sucursal ya están en el reloj — no había nada pendiente por enviar.");
            }

            AppendLog($"📤 Enviando {totalToSend} empleado(s) al reloj...");

            var sentCount = 0;
            var failed = new List<SendEmployeeFailure>();
            var processed = 0;
            var cancelled = false;
            var nextPin = 1;

            // Primero quien ya tenía PIN asignado localmente (p. ej. por "Reemplazar
            // catálogo") pero el reloj todavía no lo tiene — se respeta ese PIN, nunca se
            // reasigna uno distinto. Después, quien nunca tuvo vínculo, con PIN nuevo
            // asignado en automático. Un solo bucle sobre ambas listas (en vez de dos
            // bucles separados) para que el progreso reportado avance de forma continua
            // 1..totalToSend, sin saltos.
            var toSendInOrder = toPushExisting.Select(e => (Employee: e, IsNew: false))
                .Concat(toAutoAssign.Select(e => (Employee: e, IsNew: true)));

            foreach (var (employee, isNew) in toSendInOrder)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                progress?.Report(new SendEmployeesProgress(processed, totalToSend, employee.FullName, sentCount, failed.Count, 0));

                try
                {
                    string pin;
                    if (isNew)
                    {
                        while (usedPins.Contains(nextPin))
                        {
                            nextPin++;
                        }
                        pin = nextPin.ToString();
                    }
                    else
                    {
                        pin = mappingByEmployeeId[employee.Id].DeviceUserPin;
                    }

                    var record = new DeviceUserRecord(pin, employee.FullName, PrivilegeLevel: 0, IsEnabled: true);
                    var result = await _deviceAdapter.CreateOrUpdateUserAsync(record, cancellationToken);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        // La llamada pudo terminar (con éxito o fallo) justo cuando se pidió
                        // cancelar — se trata como "no procesado", nunca como un fallo real.
                        cancelled = true;
                        break;
                    }

                    if (result.IsFailure)
                    {
                        failed.Add(new SendEmployeeFailure(employee.FullName, result.Error.Message));
                        Log.Warning(
                            "No se pudo enviar el empleado {EmployeeId} ({FullName}, PIN {Pin}) al dispositivo {DeviceId}: {Error}",
                            employee.Id, employee.FullName, pin, device.Id, result.Error.Message);
                    }
                    else
                    {
                        if (isNew)
                        {
                            usedPins.Add(nextPin);
                            var mapping = EmployeeDeviceMapping.Create(employee.Id, device.Id, pin);
                            await _mappingRepository.AddAsync(mapping);
                        }

                        // Se guarda DE INMEDIATO, empleado por empleado — nunca se marca a
                        // nadie como sincronizado hasta esta confirmación de éxito del
                        // reloj, y así, si el proceso se cancela o la app se cierra a media
                        // corrida, lo que ya se envió con éxito queda registrado (no se
                        // reintenta ni se duplica en el siguiente envío).
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                        sentCount++;
                    }
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    // Aislado a propósito: un fallo inesperado con ESTE empleado (no una
                    // respuesta esperada del SDK, sino algo realmente imprevisto) no debe
                    // tumbar el resto del envío — se registra con todo el detalle técnico en
                    // el log y se continúa con el siguiente. El usuario nunca ve este stack
                    // trace, solo un motivo legible (ver SendEmployeeFailure.Reason).
                    Log.Error(ex,
                        "Error inesperado al enviar el empleado {EmployeeId} ({FullName}) al dispositivo {DeviceId}",
                        employee.Id, employee.FullName, device.Id);
                    failed.Add(new SendEmployeeFailure(employee.FullName, "Error inesperado — revisa el registro de errores."));
                }

                processed++;
            }

            var skipped = totalToSend - processed;
            progress?.Report(new SendEmployeesProgress(processed, totalToSend, "", sentCount, failed.Count, skipped));

            AppendLog(cancelled
                ? $"⏹️ Envío cancelado por el usuario: {sentCount} enviado(s), {failed.Count} fallido(s), {skipped} sin procesar de {totalToSend}."
                : $"✅ Enviado(s) {sentCount} de {totalToSend} empleado(s) al reloj. " +
                  "Falta enrolar su huella físicamente en el dispositivo.");
            if (failed.Count > 0)
            {
                AppendLog($"⚠️ No se pudo enviar a: {string.Join(", ", failed.Select(f => $"{f.EmployeeName} ({f.Reason})"))}.");
            }

            return new SendEmployeesOutcome(sentCount, totalToSend, skipped, failed, cancelled, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al enviar empleados al dispositivo {DeviceId}", device.Id);
            AppendLog($"⚠️ Error inesperado al enviar empleados al reloj: {ex.Message}");
            return new SendEmployeesOutcome(0, 0, 0, [], false, ex.Message);
        }
        finally
        {
            _isSendingEmployees = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Usuarios del reloj (ver quién está dado de alta directamente en la memoria
    // del dispositivo, editar/eliminar uno o varios) — pedido explícito del usuario
    // tras "Consultar información" (que solo mostraba el conteo).
    // ─────────────────────────────────────────────────────────────────────────

    public ObservableCollection<DeviceUserRow> DeviceUsers { get; } = [];

    [ObservableProperty]
    private string _deviceUsersStatusMessage = "";

    private bool _isLoadingDeviceUsers;

    /// <summary>Descarga la lista real de usuarios del dispositivo (PIN + nombre +
    /// privilegio + habilitado tal cual vive en su memoria, vía DownloadUsersAsync — el
    /// mismo método ya usado para el conteo de "Consultar información" y para evitar
    /// choques de PIN en "Enviar empleados al reloj") y refresca <see cref="DeviceUsers"/>.
    /// Guardia de reentrancia simple: no tiene sentido superponer dos descargas de la
    /// misma lista.</summary>
    public async Task LoadDeviceUsersAsync()
    {
        if (SelectedDevice is null || !IsConnected)
        {
            DeviceUsersStatusMessage = "Conecta primero con el dispositivo para ver sus usuarios.";
            return;
        }

        if (_isLoadingDeviceUsers)
        {
            return;
        }

        _isLoadingDeviceUsers = true;
        DeviceUsersStatusMessage = "Consultando usuarios del dispositivo...";
        try
        {
            var result = await _deviceAdapter.DownloadUsersAsync();
            if (result.IsFailure)
            {
                DeviceUsersStatusMessage = $"No se pudo consultar la lista: {result.Error.Message}";
                return;
            }

            DeviceUsers.Clear();
            foreach (var record in result.Value.OrderBy(u => int.TryParse(u.DeviceUserPin, out var n) ? n : int.MaxValue))
            {
                DeviceUsers.Add(new DeviceUserRow(record));
            }

            DeviceUsersStatusMessage = DeviceUsers.Count == 0
                ? "El dispositivo no tiene ningún usuario dado de alta."
                : $"{DeviceUsers.Count} usuario(s) dado(s) de alta en el dispositivo.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al consultar los usuarios del dispositivo {DeviceId}", SelectedDevice.Id);
            DeviceUsersStatusMessage = "Ocurrió un error inesperado al consultar. Revisa el registro de errores.";
        }
        finally
        {
            _isLoadingDeviceUsers = false;
        }
    }

    /// <summary>Corrige el nombre y/o el estatus habilitado de un usuario ya existente en
    /// el dispositivo — el PIN nunca se edita aquí (SSR_SetUserInfo lo usa como
    /// identificador de A CUÁL usuario escribir, no se puede "renombrar" un PIN con esta
    /// llamada; cambiarlo de verdad exigiría borrar y volver a crear, fuera de alcance
    /// para esta pantalla). El privilegio se conserva tal cual estaba.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateDeviceUserAsync(DeviceUserRow row, string newName, bool newIsEnabled)
    {
        try
        {
            var updated = new DeviceUserRecord(row.DeviceUserPin, newName, row.PrivilegeLevel, newIsEnabled);
            var result = await _deviceAdapter.CreateOrUpdateUserAsync(updated);
            if (result.IsFailure)
            {
                return result.Error.Message;
            }

            AppendLog($"✏️ Usuario del reloj actualizado: PIN {row.DeviceUserPin} — {newName}.");
            await LoadDeviceUsersAsync();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al actualizar el usuario del dispositivo (PIN={Pin})", row.DeviceUserPin);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <summary>Cambia el PIN de un usuario del reloj MOVIENDO su huella ya enrolada al PIN
    /// nuevo — pedido explícito del usuario ("editar el PIN sin ir al reloj físico" +
    /// "mover el registro de la huella al PIN nuevo"). El SDK no tiene una función para
    /// "renombrar" un PIN — el enrollNumber es la llave primaria del usuario dentro del
    /// dispositivo (ver el comentario de <see cref="UpdateDeviceUserAsync"/>) — así que esto
    /// se hace copiando:
    ///
    /// 1. Se descargan las plantillas de huella del PIN viejo.
    /// 2. Se crea el usuario nuevo (mismo nombre/privilegio/habilitado) bajo el PIN nuevo.
    /// 3. Se sube cada plantilla al PIN nuevo y se VERIFICA volviendo a descargarlas —
    ///    recién ahí se considera "movida" de verdad.
    /// 4. Solo si todo lo anterior salió bien se borra el PIN viejo del dispositivo.
    ///
    /// Este orden es deliberado: si algo falla en cualquier paso, el PIN viejo (con su
    /// huella) se queda exactamente como estaba — nunca se borra nada hasta confirmar que la
    /// copia llegó completa al PIN nuevo. Es la parte del SDK que este proyecto nunca había
    /// probado contra hardware real (ver ZKTecoDeviceAdapter.DownloadUserTemplatesAsync) —
    /// la primera vez que se use en producción, pruébalo con un PIN de prueba antes de
    /// confiar en él para un empleado real.</summary>
    /// <returns>Un mensaje de error comprensible (indicando qué quedó a medias, si aplica)
    /// si algo salió mal, o null si se movió correctamente.</returns>
    public async Task<string?> ChangeDeviceUserPinAsync(DeviceUserRow row, string newPin, CancellationToken cancellationToken = default)
    {
        var oldPin = row.DeviceUserPin;
        var trimmedNewPin = newPin.Trim();

        if (string.IsNullOrWhiteSpace(trimmedNewPin))
        {
            return "El PIN nuevo no puede estar vacío.";
        }
        if (trimmedNewPin == oldPin)
        {
            return "El PIN nuevo es igual al actual — no hay nada que cambiar.";
        }
        if (DeviceUsers.Any(u => u.DeviceUserPin == trimmedNewPin))
        {
            return $"Ya existe otro usuario con el PIN {trimmedNewPin} en este reloj — elige uno distinto.";
        }

        try
        {
            // 1) Descargar las huellas del PIN viejo — si esto falla, no se tocó nada todavía.
            var templatesResult = await _deviceAdapter.DownloadUserTemplatesAsync(oldPin, cancellationToken);
            if (templatesResult.IsFailure)
            {
                return $"No se pudieron leer las huellas del PIN {oldPin}: {templatesResult.Error.Message}. No se cambió nada.";
            }
            var templates = templatesResult.Value;

            // 2) Crear el usuario nuevo con los mismos datos.
            var newUser = new DeviceUserRecord(trimmedNewPin, row.Name, row.PrivilegeLevel, row.IsEnabled);
            var createResult = await _deviceAdapter.CreateOrUpdateUserAsync(newUser, cancellationToken);
            if (createResult.IsFailure)
            {
                return $"No se pudo crear el PIN {trimmedNewPin} en el reloj: {createResult.Error.Message}. No se cambió nada.";
            }

            // 3) Subir y VERIFICAR cada huella antes de considerar el movimiento completo —
            // el PIN viejo sigue intacto mientras esto no termine con éxito.
            foreach (var template in templates)
            {
                var uploadResult = await _deviceAdapter.UploadUserTemplateAsync(trimmedNewPin, template, cancellationToken);
                if (uploadResult.IsFailure)
                {
                    return $"El PIN {trimmedNewPin} ya se creó, pero falló al copiar una huella (dedo {template.FingerIndex}): " +
                           $"{uploadResult.Error.Message}. El PIN {oldPin} NO se tocó — sigue intacto. Revisa/elimina el PIN " +
                           $"{trimmedNewPin} (quedó incompleto) desde esta pantalla antes de reintentar.";
                }
            }

            if (templates.Count > 0)
            {
                var verifyResult = await _deviceAdapter.DownloadUserTemplatesAsync(trimmedNewPin, cancellationToken);
                if (verifyResult.IsFailure || verifyResult.Value.Count < templates.Count)
                {
                    return $"El PIN {trimmedNewPin} ya se creó, pero no se pudo confirmar que las huellas llegaron completas. " +
                           $"El PIN {oldPin} NO se tocó — sigue intacto. Revisa/elimina el PIN {trimmedNewPin} (quedó incompleto) " +
                           "desde esta pantalla antes de reintentar.";
                }
            }

            // 4) Recién aquí, con el PIN nuevo confirmado completo, se borra el viejo. Un
            // fallo en este paso puntual no es grave — la huella ya quedó a salvo en el PIN
            // nuevo, solo queda un PIN viejo huérfano que hay que borrar a mano.
            var deleteResult = await _deviceAdapter.DeleteUserAsync(oldPin, cancellationToken);
            if (deleteResult.IsFailure)
            {
                AppendLog($"⚠️ PIN {oldPin} movido a {trimmedNewPin}, pero no se pudo borrar el PIN viejo del reloj: {deleteResult.Error.Message} — bórralo a mano.");
            }

            // Corrige el vínculo local (Empleado↔PIN) para que las marcaciones FUTURAS (que
            // llegarán con el PIN nuevo) se sigan resolviendo al mismo empleado — ver
            // EmployeeDeviceMapping.UpdatePin. Las marcaciones YA guardadas conservan el PIN
            // viejo tal cual ocurrieron, deliberadamente no se tocan (reflejan un hecho
            // histórico real, no el estado actual del vínculo).
            if (SelectedDevice is not null)
            {
                var mappings = await _mappingRepository.ListAsync(cancellationToken);
                var mapping = mappings.FirstOrDefault(m => m.DeviceId == SelectedDevice.Id && m.DeviceUserPin == oldPin);
                if (mapping is not null)
                {
                    var tracked = await _mappingRepository.GetByIdAsync(mapping.Id, cancellationToken);
                    if (tracked is not null)
                    {
                        tracked.UpdatePin(trimmedNewPin);
                        await _unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                }
            }

            AppendLog($"🔁 PIN {oldPin} → {trimmedNewPin} ({row.Name}) — {templates.Count} huella(s) movida(s).");
            await LoadDeviceUsersAsync();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al cambiar el PIN del dispositivo ({OldPin} -> {NewPin})", oldPin, trimmedNewPin);
            return "Ocurrió un error inesperado al cambiar el PIN. Revisa el registro de errores y el estado real en " +
                   "\"Usuarios del reloj\" antes de reintentar — no está garantizado qué tanto del cambio alcanzó a aplicarse.";
        }
    }

    /// <summary>Elimina uno o varios usuarios del dispositivo (mismo método tanto para
    /// "Eliminar" individual como para la selección masiva — un solo camino, sin duplicar
    /// lógica). Un fallo en un PIN no detiene el resto del lote; se reportan todos los
    /// fallos juntos al final. Deliberadamente NO toca EmployeeDeviceMapping en la base
    /// local — borrar a alguien del reloj no borra su historial de asistencia ya
    /// guardado, y si se vuelve a dar de alta con el mismo PIN el vínculo local sigue
    /// siendo válido.</summary>
    /// <returns>(cuántos se eliminaron con éxito, nombres de los que fallaron con su motivo)</returns>
    public async Task<(int Deleted, IReadOnlyList<string> Failed)> DeleteDeviceUsersAsync(IReadOnlyList<DeviceUserRow> rows)
    {
        var deleted = 0;
        var failed = new List<string>();

        foreach (var row in rows)
        {
            try
            {
                var result = await _deviceAdapter.DeleteUserAsync(row.DeviceUserPin);
                if (result.IsFailure)
                {
                    failed.Add($"PIN {row.DeviceUserPin} ({row.Name}): {result.Error.Message}");
                    continue;
                }

                deleted++;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error inesperado al eliminar el usuario del dispositivo (PIN={Pin})", row.DeviceUserPin);
                failed.Add($"PIN {row.DeviceUserPin} ({row.Name}): error inesperado — {ex.Message}");
            }
        }

        if (deleted > 0)
        {
            AppendLog($"🗑️ {deleted} usuario(s) eliminado(s) del reloj.");
        }

        await LoadDeviceUsersAsync();
        return (deleted, failed);
    }

    private void AppendLog(string message) =>
        LogEntries.Insert(0, $"{DateTime.Now:HH:mm:ss} — {message}");

    private void RefreshStatusMessage()
    {
        if (_allDevices.Count == 0)
        {
            StatusMessage = "Aún no hay dispositivos registrados.";
            return;
        }

        var hiddenCount = _allDevices.Count - Devices.Count;
        StatusMessage = hiddenCount > 0
            ? $"{Devices.Count} de {_allDevices.Count} dispositivo(s) — {hiddenCount} deshabilitado(s) oculto(s) (\"Mostrar deshabilitados\")."
            : $"{Devices.Count} dispositivo(s) registrado(s) en la base local.";
    }
}
