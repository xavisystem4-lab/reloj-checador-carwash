using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Infrastructure.Devices.ZKTeco;

/// <summary>
/// Adaptador real para relojes ZKTeco (probado en campo contra un F22/ID) usando el
/// componente COM oficial <c>zkemkeeper.dll</c> (ver third-party/zkteco-sdk/README.md).
///
/// <para><b>Por qué "dynamic" y no una referencia COM generada (tlbimp/Interop.dll):</b>
/// generar el ensamblado de interop requiere que <c>zkemkeeper.dll</c> esté registrado en
/// la máquina que compila — imposible desde macOS/Linux (donde también compila el resto
/// del proyecto) y frágil incluso en CI. Con enlace tardío (<see cref="Type.GetTypeFromProgID"/>
/// + <c>dynamic</c>) el proyecto compila igual en cualquier sistema operativo; el SDK real
/// solo hace falta en tiempo de EJECUCIÓN, en la máquina Windows de la sucursal — donde el
/// instalador ya lo registra (ver installer/RelojChecador.iss).</para>
///
/// <para><b>Por qué 32 bits:</b> zkemkeeper.dll es un COM server de 32 bits. Un proceso
/// .NET de 64 bits no puede activarlo. Por eso RelojChecador.WPF se publica como
/// <c>win-x86</c>, no <c>win-x64</c> — ver .github/workflows/build.yml.</para>
///
/// <para><b>Por qué monitoreo por sondeo (polling) y no el evento COM <c>OnAttTransactionEx</c>:</b>
/// los eventos COM (connection points) necesitan una interfaz de evento fuertemente tipada
/// para poder suscribirse con <c>+=</c> — eso exige el ensamblado de interop generado que
/// justo estamos evitando por el punto anterior. Sondear <c>ReadGeneralLogData</c> cada
/// pocos segundos es el patrón más usado en integraciones de este SDK en la práctica (más
/// estable entre versiones de firmware que el modo "tiempo real" nativo) y cumple el
/// requisito de que la marcación aparezca casi al instante sin depender de un botón.</para>
///
/// <para><b>Nombres de método y códigos del SDK sin verificar contra hardware real:</b> el
/// mapeo de <c>dwVerifyMode</c> a <see cref="VerifyMethod"/> y el "backup number" de
/// <c>SSR_DeleteEnrollData</c> siguen la convención más citada en la documentación/comunidad
/// del SDK, pero no se probaron todavía contra el F22/ID real — quedan marcados donde
/// aplica. Todo lo demás (Connect_Net, ReadGeneralLogData/GetGeneralLogData,
/// ReadAllUserID/SSR_GetAllUserInfo, SSR_SetUserInfo, EnableDevice, RestartDevice,
/// ClearGLog) es la API pública documentada del SDK.</para>
/// </summary>
public sealed class ZKTecoDeviceAdapter : IAttendanceDeviceAdapter, IDisposable
{
    // Confirmado en Windows real (no era la convención más citada que se había asumido
    // aquí — "zkemkeeper.CZKEM" resultó incorrecto): consultando el registro tras un
    // regsvr32 exitoso, el CLSID {00853A19-BD51-419B-9269-2DABE57EB61F} de zkemkeeper.dll
    // tiene VersionIndependentProgID "zkemkeeper.ZKEM" (y ProgID versionado
    // "zkemkeeper.ZKEM.1"). Se usa el VersionIndependentProgID, la práctica estándar para
    // enlace tardío — siempre apunta a la versión más reciente registrada.
    private const string ProgId = "zkemkeeper.ZKEM";
    private const int MachineNumber = 1; // Un solo dispositivo por conexión Connect_Net — convención del SDK.

    // Enlace tardío a propósito (ver comentario de la clase) — dynamic, no un tipo generado.
    private dynamic? _zk;
    private bool _isConnected;
    private Timer? _realTimeTimer;
    private DateTime _realTimeSinceUtc;
    private readonly SemaphoreSlim _logAccessLock = new(1, 1);

    public string Brand => "ZKTeco";

    public event EventHandler<RawAttendanceRecord>? AttendancePunchReceived;

    // Devuelve Result<object>, NO Result<dynamic> — encontrado como bug real en Windows
    // (ver commit): cuando el argumento de Result.Success(...) es de tipo "dynamic", el
    // compilador liga esa llamada en tiempo de ejecución (DLR) y el genérico T se infiere
    // del tipo EN TIEMPO DE EJECUCIÓN del valor (aquí, System.__ComObject — el wrapper que
    // usa .NET para objetos COM), no del tipo estático declarado. El resultado real en
    // ejecución termina siendo Result<System.__ComObject>, y como Result<T> es invariante
    // (una clase normal, sin "out T"), no hay conversión implícita hacia Result<object>/
    // Result<dynamic> — el binder revienta con RuntimeBinderException: "Cannot implicitly
    // convert type 'Result<System.__ComObject>' to 'Result<object>'." Fix: asignar el
    // valor COM a una variable de tipo "object" ANTES de pasarlo a Result.Success — eso
    // corta la cadena de "dynamic" en ese punto y fuerza resolución estática normal (T se
    // infiere como object, sin pasar por el DLR).
    private Result<object> EnsureComObject()
    {
        if (_zk is not null)
        {
            object existing = _zk;
            return Result.Success(existing);
        }

        if (!OperatingSystem.IsWindows())
        {
            return Result.Failure<object>(DeviceErrors.SdkNotAvailable("esta plataforma no es Windows"));
        }

        try
        {
            var type = Type.GetTypeFromProgID(ProgId);
            if (type is null)
            {
                return Result.Failure<object>(
                    DeviceErrors.SdkNotAvailable($"no se encontró el ProgID '{ProgId}' en el registro de Windows"));
            }

            _zk = Activator.CreateInstance(type);
            object created = _zk!;
            return Result.Success(created);
        }
        catch (COMException ex)
        {
            return Result.Failure<object>(DeviceErrors.SdkNotAvailable(ex.Message));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result.Failure<object>(DeviceErrors.SdkNotAvailable(ex.Message));
        }
    }

    public async Task<Result> ConnectAsync(DeviceConnectionInfo connection, CancellationToken cancellationToken = default)
    {
        var comResult = EnsureComObject();
        if (comResult.IsFailure)
        {
            return Result.Failure(comResult.Error);
        }

        return await Task.Run(() =>
        {
            try
            {
                bool ok = _zk!.Connect_Net(connection.IpAddress, connection.TcpPort);
                if (!ok)
                {
                    return Result.Failure(DeviceErrors.AuthenticationFailed());
                }

                _isConnected = true;
                return Result.Success();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"Connect_Net falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await StopRealTimeMonitoringAsync(cancellationToken);

        if (_zk is null)
        {
            _isConnected = false;
            return Result.Success();
        }

        return await Task.Run(() =>
        {
            try
            {
                _zk!.Disconnect();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Desconectar nunca debe dejar a la app sin poder salir de la pantalla —
                // se registra la falla mental (nada que loggear aquí sin Serilog inyectado)
                // pero igual se marca como desconectado localmente.
                _ = ex;
            }
            finally
            {
                _isConnected = false;
            }

            return Result.Success();
        }, cancellationToken);
    }

    /// <summary>Independiente del SDK de ZKTeco a propósito — un ping ICMP es el mismo
    /// primer nivel de diagnóstico sin importar la marca del reloj.</summary>
    public async Task<Result<NetworkTestResult>> TestNetworkAsync(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!System.Net.IPAddress.TryParse(ipAddress, out _))
        {
            return Result.Failure<NetworkTestResult>(DeviceErrors.InvalidIpAddress(ipAddress));
        }

        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 2000);
            var now = DateTime.UtcNow;

            return Result.Success(reply.Status == IPStatus.Success
                ? new NetworkTestResult(IsReachable: true, RoundTripTime: TimeSpan.FromMilliseconds(reply.RoundtripTime), ErrorMessage: null, TestedAtUtc: now)
                : new NetworkTestResult(IsReachable: false, RoundTripTime: null, ErrorMessage: reply.Status.ToString(), TestedAtUtc: now));
        }
        catch (PingException ex)
        {
            return Result.Success(new NetworkTestResult(IsReachable: false, RoundTripTime: null, ErrorMessage: ex.Message, TestedAtUtc: DateTime.UtcNow));
        }
    }

    /// <summary>También independiente del SDK — abrir un socket TCP no necesita el
    /// componente COM de ZKTeco.</summary>
    public async Task<Result<TcpPortTestResult>> TestTcpPortAsync(
        string ipAddress, int tcpPort, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTime.UtcNow;
        using var client = new TcpClient();
        try
        {
            var connectTask = client.ConnectAsync(ipAddress, tcpPort, cancellationToken).AsTask();
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            var finished = await Task.WhenAny(connectTask, timeoutTask);
            var elapsed = DateTime.UtcNow - startedAt;

            if (finished == timeoutTask || !client.Connected)
            {
                return Result.Success(new TcpPortTestResult(IsOpen: false, Elapsed: elapsed,
                    ErrorMessage: $"Tiempo de espera agotado conectando al puerto {tcpPort}.", TestedAtUtc: DateTime.UtcNow));
            }

            return Result.Success(new TcpPortTestResult(IsOpen: true, Elapsed: elapsed, ErrorMessage: null, TestedAtUtc: DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            return Result.Success(new TcpPortTestResult(IsOpen: false, Elapsed: DateTime.UtcNow - startedAt,
                ErrorMessage: ex.Message, TestedAtUtc: DateTime.UtcNow));
        }
    }

    public async Task<Result<DeviceInfo>> GetDeviceInformationAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure<DeviceInfo>(DeviceErrors.NotConnected());
        }

        return await Task.Run(async () =>
        {
            try
            {
                string serial = "";
                string firmware = "";
                string platform = "";
                _zk!.GetSerialNumber(MachineNumber, out serial);
                _zk!.GetFirmwareVersion(MachineNumber, out firmware);
                _zk!.GetPlatform(MachineNumber, out platform);

                // Se reutilizan las descargas completas (en vez de GetDeviceStatus con
                // códigos numéricos de dwStatus) porque esos códigos varían entre
                // versiones del SDK y no se pudieron verificar contra hardware real —
                // esto es más lento pero da un conteo que sabemos correcto.
                var usersResult = await DownloadUsersAsync(cancellationToken);
                var logsResult = await DownloadAttendanceLogsAsync(cancellationToken);

                var info = new DeviceInfo(
                    SerialNumber: string.IsNullOrWhiteSpace(serial) ? null : serial,
                    FirmwareVersion: string.IsNullOrWhiteSpace(firmware) ? null : firmware,
                    Platform: string.IsNullOrWhiteSpace(platform) ? null : platform,
                    FingerprintAlgorithm: null, // El SDK no expone esto de forma directa/confiable.
                    Manufacturer: "ZKTECO CO., LTD.",
                    RegisteredUserCount: usersResult.IsSuccess ? usersResult.Value.Count : null,
                    StoredAttendanceLogCount: logsResult.IsSuccess ? logsResult.Value.Count : null,
                    StoredFingerprintTemplateCount: null);

                return Result.Success(info);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure<DeviceInfo>(Error.Unexpected($"No se pudo leer información del dispositivo: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result<DateTime>> GetDeviceTimeAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure<DateTime>(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                // object, no int — mismo motivo que ReadAllGeneralLogEntries (ver ese
                // comentario): confirmado en Windows real que "ref int" falla con
                // GetGeneralLogData; se aplica el mismo tratamiento aquí preventivamente,
                // aunque GetDeviceTime en sí todavía no se ha probado contra hardware real.
                object year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
                bool ok = _zk!.GetDeviceTime(MachineNumber, ref year, ref month, ref day, ref hour, ref minute, ref second);
                if (!ok)
                {
                    return Result.Failure<DateTime>(Error.Unexpected("GetDeviceTime devolvió falso."));
                }

                return Result.Success(new DateTime(
                    Convert.ToInt32(year), Convert.ToInt32(month), Convert.ToInt32(day),
                    Convert.ToInt32(hour), Convert.ToInt32(minute), Convert.ToInt32(second), DateTimeKind.Unspecified));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure<DateTime>(Error.Unexpected($"GetDeviceTime falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> SetDeviceTimeAsync(DateTime utcTime, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                bool ok = _zk!.SetDeviceTime2(
                    MachineNumber, utcTime.Year, utcTime.Month, utcTime.Day, utcTime.Hour, utcTime.Minute, utcTime.Second);
                return ok ? Result.Success() : Result.Failure(Error.Unexpected("SetDeviceTime2 devolvió falso."));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"SetDeviceTime2 falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<RawAttendanceRecord>>> DownloadAttendanceLogsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure<IReadOnlyList<RawAttendanceRecord>>(DeviceErrors.NotConnected());
        }

        await _logAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    var records = ReadAllGeneralLogEntries();
                    return Result.Success<IReadOnlyList<RawAttendanceRecord>>(records);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Result.Failure<IReadOnlyList<RawAttendanceRecord>>(
                        Error.Unexpected($"No se pudo descargar la bitácora de asistencias: {DescribeException(ex)}"));
                }
            }, cancellationToken);
        }
        finally
        {
            _logAccessLock.Release();
        }
    }

    /// <summary>Formatea una excepción con todo el detalle disponible (tipo, HRESULT si es
    /// COMException, y la cadena completa de InnerException) — el mensaje corto por
    /// defecto de "Error while invoking X" ya demostró no bastar para diagnosticar un
    /// fallo real de COM sin acceso directo a la máquina Windows donde ocurre.</summary>
    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        while (current is not null)
        {
            var hresult = current is COMException or ExternalException
                ? $" (HRESULT 0x{current.HResult:X8})"
                : "";
            parts.Add($"{current.GetType().Name}{hresult}: {current.Message}");
            current = current.InnerException;
        }

        return string.Join(" → ", parts);
    }

    /// <summary>Recorre el buffer de bitácora del dispositivo. Debe llamarse ya con
    /// <see cref="_logAccessLock"/> tomado — el SDK no es seguro para lecturas
    /// concurrentes del mismo buffer (por eso el sondeo en tiempo real y la descarga
    /// manual comparten el mismo candado).</summary>
    // 11 parámetros de GetGeneralLogData: MachineNumber (entrada) + 10 "ref" de salida.
    // Se marcan como ParameterModifier(isByRef) explícitos para Type.InvokeMember — ver
    // comentario de ReadAllGeneralLogEntries sobre por qué se abandonó "dynamic" para esta
    // llamada en particular.
    private static readonly ParameterModifier[] GeneralLogDataParameterModifiers = CreateGeneralLogDataParameterModifiers();

    private static ParameterModifier[] CreateGeneralLogDataParameterModifiers()
    {
        var modifier = new ParameterModifier(11);
        for (var i = 1; i < 11; i++)
        {
            modifier[i] = true;
        }

        return new[] { modifier };
    }

    /// <summary>Recorre el buffer de bitácora del dispositivo. Debe llamarse ya con
    /// <see cref="_logAccessLock"/> tomado — el SDK no es seguro para lecturas
    /// concurrentes del mismo buffer (por eso el sondeo en tiempo real y la descarga
    /// manual comparten el mismo candado).
    ///
    /// <para><b>Historial de intentos contra hardware real</b> (F22/ID, SDK zkemkeeper,
    /// plataforma ZLM60_TFT): (1) v1.1.0, "ref" de GetGeneralLogData tipados como
    /// "int"/"string" vía "dynamic" → "Could not convert argument 9 for call to
    /// GetGeneralLogData" (fallo de conversión de tipos, detectado ANTES de invocar el
    /// método COM). (2) v1.1.0, mismos "ref" tipados como "object" → EXACTO el mismo
    /// error, con el binario ya reconstruido — descarta que fuera el tipo CLR del local.
    /// (3) v1.2.0, método alternativo GetGeneralLogDataStr (variante que devuelve BSTR en
    /// vez de VARIANT numérico) → error DISTINTO, "Error while invoking
    /// GetGeneralLogDataStr" (ya no es un fallo de conversión de tipos sino de la
    /// invocación COM en sí, dentro de IDispatch::Invoke). Con dos fallos distintos usando
    /// "dynamic" (el keyword de C#, resuelto por el DLR/ComBinder), se sospecha del
    /// MECANISMO de llamada, no solo de los tipos o el nombre del método — así que esta
    /// versión cambia a <see cref="Type.InvokeMember"/> con <see cref="ParameterModifier"/>
    /// explícito, la forma clásica (pre-C#4 dynamic) de invocar Automation COM por enlace
    /// tardío, con control total sobre qué argumentos son "ref" en vez de depender del
    /// binder implícito de "dynamic". (4) v1.3.0, Type.InvokeMember con los campos
    /// numéricos boxeados como "int" (Int32/VT_I4) → <c>DISP_E_TYPEMISMATCH</c>
    /// (0x80020005). (5) v1.4.0, exactamente lo mismo pero boxeados como "short"
    /// (Int16/VT_I2) → EL MISMO error, byte por byte — dato clave: si el problema fuera el
    /// ANCHO del entero (Int16 vs Int32), cambiarlo debería haber dado éxito o un error
    /// DISTINTO; que sea idéntico descarta esa hipótesis y apunta a otra cosa: el
    /// parámetro no espera ningún tipo numérico concreto, sino un VARIANT genérico
    /// (<c>VT_VARIANT</c>) — típico de interfaces Automation pensadas para VB6/VBScript,
    /// donde todos los parámetros son "Variant" por convención. Marshalear un valor
    /// boxeado como "short"/"int" vía Type.InvokeMember NO produce VT_VARIANT (el
    /// marshaler usa el tipo real del valor boxeado, sea cual sea, para elegir un VARTYPE
    /// concreto) — hace falta <see cref="VariantWrapper"/> explícito para forzar
    /// VT_VARIANT sin importar el tipo interno.</para>
    /// </summary>
    private List<RawAttendanceRecord> ReadAllGeneralLogEntries()
    {
        var records = new List<RawAttendanceRecord>();

        bool hasData = _zk!.ReadGeneralLogData(MachineNumber);
        if (!hasData)
        {
            return records;
        }

        object comObject = _zk!;
        var comType = comObject.GetType();

        while (true)
        {
            // Un array nuevo en cada vuelta, a propósito: tras la llamada, InvokeMember
            // reemplaza cada posición "ref" por el valor de salida SIN volver a envolverlo
            // en VariantWrapper (el wrapper es solo una instrucción de marshaling para el
            // valor de ENTRADA) — si se reutilizara el mismo array entre vueltas, la
            // siguiente llamada perdería el forzado a VT_VARIANT justo donde más importa.
            object?[] args =
            {
                MachineNumber,
                new VariantWrapper(""), new VariantWrapper((short)0), new VariantWrapper((short)0),
                new VariantWrapper((short)0), new VariantWrapper((short)0), new VariantWrapper((short)0),
                new VariantWrapper((short)0), new VariantWrapper((short)0), new VariantWrapper((short)0),
                new VariantWrapper((short)0),
            };

            var hasMore = (bool)comType.InvokeMember(
                "GetGeneralLogData", BindingFlags.InvokeMethod, binder: null, target: comObject,
                args: args, modifiers: GeneralLogDataParameterModifiers, culture: null, namedParameters: null)!;
            if (!hasMore)
            {
                break;
            }

            var pin = Convert.ToString(args[1]) ?? "";
            var verifyModeInt = Convert.ToInt32(args[2]);
            var inOutModeInt = Convert.ToInt32(args[3]);
            var timestamp = new DateTime(
                Convert.ToInt32(args[4]), Convert.ToInt32(args[5]), Convert.ToInt32(args[6]),
                Convert.ToInt32(args[7]), Convert.ToInt32(args[8]), Convert.ToInt32(args[9]), DateTimeKind.Unspecified);
            records.Add(new RawAttendanceRecord(
                pin, timestamp, MapVerifyMode(verifyModeInt), inOutModeInt,
                RawPayload: $"ZK|{pin}|{verifyModeInt}|{inOutModeInt}|{timestamp:o}"));
        }

        return records;
    }

    /// <summary>Mapeo best-effort — la convención más citada del SDK (1=huella,
    /// 3=contraseña, 4=tarjeta), sin confirmar todavía contra el F22/ID real. Cualquier
    /// valor no reconocido se guarda como Unknown en vez de adivinar.</summary>
    private static VerifyMethod MapVerifyMode(int verifyMode) => verifyMode switch
    {
        1 => VerifyMethod.Fingerprint,
        3 => VerifyMethod.Password,
        4 => VerifyMethod.Card,
        15 => VerifyMethod.Face,
        _ => VerifyMethod.Unknown,
    };

    public async Task<Result<IReadOnlyList<DeviceUserRecord>>> DownloadUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure<IReadOnlyList<DeviceUserRecord>>(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                var users = new List<DeviceUserRecord>();
                bool hasData = _zk!.ReadAllUserID(MachineNumber);
                if (hasData)
                {
                    string enrollNumber = "", name = "", password = "";
                    int privilege = 0;
                    bool enabled = true;

                    while (_zk!.SSR_GetAllUserInfo(
                               MachineNumber, ref enrollNumber, ref name, ref password, ref privilege, ref enabled))
                    {
                        users.Add(new DeviceUserRecord(enrollNumber, name, privilege, enabled));
                    }
                }

                return Result.Success<IReadOnlyList<DeviceUserRecord>>(users);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure<IReadOnlyList<DeviceUserRecord>>(
                    Error.Unexpected($"No se pudo descargar la lista de usuarios: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> CreateOrUpdateUserAsync(DeviceUserRecord user, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                bool ok = _zk!.SSR_SetUserInfo(
                    MachineNumber, user.DeviceUserPin, user.Name, /* password */ "", user.PrivilegeLevel, user.IsEnabled);
                return ok ? Result.Success() : Result.Failure(Error.Unexpected("SSR_SetUserInfo devolvió falso."));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"SSR_SetUserInfo falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> DeleteUserAsync(string deviceUserPin, CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                // dwBackupNumber = 12: convención citada para "borrar todo lo del usuario"
                // (huellas + tarjeta + contraseña + su entrada), sin confirmar contra
                // hardware real todavía.
                bool ok = _zk!.SSR_DeleteEnrollData(MachineNumber, deviceUserPin, 12);
                return ok ? Result.Success() : Result.Failure(DeviceErrors.UserNotFound(deviceUserPin));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"SSR_DeleteEnrollData falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> EnableDeviceAsync(CancellationToken cancellationToken = default) =>
        await ToggleEnabledAsync(true, cancellationToken);

    public async Task<Result> DisableDeviceAsync(CancellationToken cancellationToken = default) =>
        await ToggleEnabledAsync(false, cancellationToken);

    private async Task<Result> ToggleEnabledAsync(bool enable, CancellationToken cancellationToken)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                bool ok = _zk!.EnableDevice(MachineNumber, enable);
                return ok ? Result.Success() : Result.Failure(Error.Unexpected("EnableDevice devolvió falso."));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"EnableDevice falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> RestartDeviceAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        return await Task.Run(() =>
        {
            try
            {
                _zk!.RestartDevice(MachineNumber);
                return Result.Success();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                return Result.Failure(Error.Unexpected($"RestartDevice falló: {ex.Message}"));
            }
        }, cancellationToken);
    }

    public async Task<Result> ClearAttendanceLogsAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Result.Failure(DeviceErrors.NotConnected());
        }

        await _logAccessLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool ok = _zk!.ClearGLog(MachineNumber);
                    return ok ? Result.Success() : Result.Failure(Error.Unexpected("ClearGLog devolvió falso."));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    return Result.Failure(Error.Unexpected($"ClearGLog falló: {ex.Message}"));
                }
            }, cancellationToken);
        }
        finally
        {
            _logAccessLock.Release();
        }
    }

    public Task<DeviceCapabilities> GetSupportedCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        // A propósito NO incluye FingerprintTemplateTransfer ni UserPhotoSync: el SDK las
        // soporta, pero este adaptador todavía no las implementa — mejor no anunciar una
        // capacidad que la app no puede cumplir.
        const DeviceCapabilities implemented =
            DeviceCapabilities.DownloadAttendanceLogs |
            DeviceCapabilities.DownloadUsers |
            DeviceCapabilities.ManageUsers |
            DeviceCapabilities.SetDeviceTime |
            DeviceCapabilities.RemoteRestart |
            DeviceCapabilities.EnableDisable |
            DeviceCapabilities.ClearAttendanceLogs |
            DeviceCapabilities.RealTimeEvents;

        return Task.FromResult(implemented);
    }

    public Task<Result> StartRealTimeMonitoringAsync(CancellationToken cancellationToken = default)
    {
        if (!_isConnected)
        {
            return Task.FromResult(Result.Failure(DeviceErrors.NotConnected()));
        }

        if (_realTimeTimer is not null)
        {
            return Task.FromResult(Result.Success()); // ya estaba monitoreando
        }

        // Solo se reportan marcaciones desde este momento en adelante — el histórico se
        // consulta aparte con DownloadAttendanceLogsAsync, no se reproduce por este medio.
        _realTimeSinceUtc = DateTime.UtcNow;
        _realTimeTimer = new Timer(_ => _ = PollForNewPunchesAsync(), null, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> StopRealTimeMonitoringAsync(CancellationToken cancellationToken = default)
    {
        _realTimeTimer?.Dispose();
        _realTimeTimer = null;
        return Task.FromResult(Result.Success());
    }

    /// <summary>Callback del Timer de sondeo: relee la bitácora completa y emite (vía
    /// <see cref="AttendancePunchReceived"/>) solo lo que sea más nuevo que la última
    /// marcación ya reportada. No usa un candado bloqueante síncrono con la descarga
    /// manual (_logAccessLock es un SemaphoreSlim asíncrono) — si coinciden, uno espera
    /// al otro en vez de corromper la lectura del buffer del dispositivo.</summary>
    private async Task PollForNewPunchesAsync()
    {
        if (_zk is null || !_isConnected)
        {
            return;
        }

        await _logAccessLock.WaitAsync();
        try
        {
            var records = ReadAllGeneralLogEntries();
            var newOnes = records.Where(r => r.TimestampUtc > _realTimeSinceUtc).OrderBy(r => r.TimestampUtc).ToList();
            if (newOnes.Count == 0)
            {
                return;
            }

            _realTimeSinceUtc = newOnes[^1].TimestampUtc;
            foreach (var record in newOnes)
            {
                AttendancePunchReceived?.Invoke(this, record);
            }
        }
        catch
        {
            // El sondeo se reintenta solo en el siguiente tick del Timer — una falla
            // puntual (p. ej. el reloj se desconectó de la red un instante) no debe tumbar
            // el Timer ni la app.
        }
        finally
        {
            _logAccessLock.Release();
        }
    }

    public void Dispose()
    {
        _realTimeTimer?.Dispose();
        _logAccessLock.Dispose();

        if (_zk is not null && OperatingSystem.IsWindows())
        {
            try
            {
                Marshal.ReleaseComObject(_zk);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _ = ex;
            }
        }

        _zk = null;
    }
}
