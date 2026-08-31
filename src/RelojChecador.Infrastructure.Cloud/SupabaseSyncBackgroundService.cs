using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Identity;
using RelojChecador.Application.Payroll;
using RelojChecador.Application.Sync;
using RelojChecador.Infrastructure.Cloud.Dtos;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Empuja periódicamente los cambios locales hacia Supabase — nunca al revés (v1 es
/// push-only: la app de escritorio es la única fuente de verdad, el Dashboard solo lee).
/// Única excepción deliberada: <see cref="TryDeleteAttendancesRemoteAsync"/>, para que un
/// borrado explícito del administrador SÍ se refleje en el Dashboard (pedido explícito del
/// usuario: "podemos borrar en el sistema y que también mande la señal al sitio web").
/// Cada ciclo va en su propio try/catch: sin conexión a internet es un caso ESPERADO
/// (operación offline-first, ver README), no un error que deba tumbar el host ni
/// interrumpir el uso normal de la app — simplemente se reintenta en el siguiente ciclo.
///
/// Branches/Employees/Devices/EmployeeDeviceMappings/Users se reenvían completos en cada
/// ciclo (son tablas chicas en un negocio de una sola sucursal-tipo; el costo de red es
/// insignificante). Attendances puede crecer mucho con el tiempo, así que usa un cursor
/// incremental (ISyncCursorStore) y se drena en lotes.
/// </summary>
public sealed class SupabaseSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    SupabaseSyncOptions options,
    SupabaseSyncStatus status,
    ILogger<SupabaseSyncBackgroundService> logger) : BackgroundService
{
    private const int AttendanceBatchSize = 500;
    private const string AttendanceCursorKey = "Attendance";

    // Evita que el botón "Conectar con nube" (TriggerSyncNowAsync, disparado desde la UI)
    // y el ciclo automático de este mismo servicio corran a la vez — cada uno crea su
    // propio DbContext (scopeFactory.CreateScope()) y dos ciclos escribiendo el cursor de
    // asistencias al mismo tiempo podrían pisarse entre sí.
    private readonly SemaphoreSlim _runLock = new(1, 1);

    // Antes de esto, un false aquí terminaba ExecuteAsync para siempre (BackgroundService no
    // se reinicia solo) — si la app arrancaba sin ServiceRoleKey configurada, activarla más
    // tarde (ver "Conectar con nube"/SupabaseLocalConfigStore) nunca reactivaba el ciclo
    // automático sin reiniciar la app entera. Se guarda para solo loguear el cambio de
    // estado UNA vez, no en cada vuelta del sondeo.
    private bool? _wasConfiguredLastTick;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.IsConfigured)
            {
                if (_wasConfiguredLastTick != false)
                {
                    logger.LogInformation(
                        "Sincronización con Supabase deshabilitada (falta Url o ServiceRoleKey en " +
                        "%LocalAppData%\\RelojChecador\\appsettings.Local.json) — la app sigue funcionando " +
                        "100% local, sin nube por ahora. Se revisa de nuevo cada pocos segundos por si se " +
                        "activa desde \"Conectar con nube\" sin necesidad de reiniciar.");
                    _wasConfiguredLastTick = false;
                }
                status.MarkDisabled();
            }
            else
            {
                if (_wasConfiguredLastTick != true)
                {
                    logger.LogInformation(
                        "Sincronización con Supabase activa. Intervalo: {IntervalSeconds}s.", options.IntervalSeconds);
                    _wasConfiguredLastTick = true;
                }
                await RunCycleAsync(stoppingToken);
            }

            try
            {
                // Mientras no esté configurado, sondea cada 5s (barato, sin red) en vez de
                // esperar IntervalSeconds completo — así "Conectar con nube" se siente
                // instantáneo la primera vez que alguien lo usa.
                var delaySeconds = options.IsConfigured ? options.IntervalSeconds : 5;
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Dispara un ciclo de sincronización de inmediato, sin esperar al siguiente
    /// tick automático. Tres llamadores: el botón "Conectar con nube" de la barra superior
    /// (ver MainWindow/UpdateViewModel), para poder probar en el momento en vez de esperar
    /// hasta 10s (IntervalSeconds); DevicesViewModel, cada vez que se guarda una marcación
    /// nueva (tiempo real o descarga manual) — así el Dashboard la ve casi al instante en
    /// vez de esperar el próximo ciclo automático; y DevicesViewModel de nuevo al procesar
    /// una solicitud de sincronización remota (ver RemoteSyncRequestCoordinator), que
    /// necesita saber si el push realmente funcionó para reportar la solicitud como
    /// completada o fallida — de ahí que devuelva bool en vez de Task simple. Si la
    /// sincronización no está configurada, deja constancia de eso en
    /// <see cref="SupabaseSyncStatus"/> igual que el ciclo automático, en vez de lanzar una
    /// excepción — nunca debe tumbar la UI.</summary>
    public async Task<bool> TriggerSyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured)
        {
            status.MarkDisabled();
            return false;
        }

        return await RunCycleAsync(cancellationToken);
    }

    /// <summary>Borra directamente en Supabase las filas de asistencia con estos ids — pedido
    /// explícito del usuario: "podemos borrar en el sistema y que también mande la señal al
    /// sitio web". Única excepción DELIBERADA al resto de este motor (push-only, nunca
    /// borra — ver comentario de clase): un borrado explícito del administrador SÍ debe
    /// reflejarse de inmediato en el Dashboard. El borrado LOCAL (ver
    /// AttendanceViewModel.DeleteAttendanceAsync/BulkDeleteAsync) ya ocurrió antes de llamar
    /// esto y es lo que de verdad importa — si esto falla (sin Supabase configurado, sin
    /// internet, etc.) no se lanza: la fila queda huérfana en el Dashboard hasta el próximo
    /// intento manual, igual que cualquier otro fallo de red de este motor.</summary>
    /// <returns>true si el borrado remoto se confirmó; false si no se intentó (no
    /// configurado, lista vacía) o si falló.</returns>
    public async Task<bool> TryDeleteAttendancesRemoteAsync(
        IReadOnlyList<Guid> attendanceIds, CancellationToken cancellationToken = default)
    {
        if (!options.IsConfigured || attendanceIds.Count == 0)
        {
            return false;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var restClient = scope.ServiceProvider.GetRequiredService<SupabaseRestClient>();

            // PostgREST: "id=in.(id1,id2,...)" borra varias filas en una sola petición.
            var idsList = string.Join(",", attendanceIds);
            await restClient.DeleteAsync("attendances", $"id=in.({idsList})", cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "No se pudo borrar en Supabase {Count} marcación(es) — quedan huérfanas ahí hasta el próximo intento manual.",
                attendanceIds.Count);
            return false;
        }
    }

    /// <returns>true si el ciclo completo (todas las tablas) subió sin errores; false si
    /// algo falló — ver <see cref="SupabaseSyncStatus.LastError"/> para el detalle.</returns>
    private async Task<bool> RunCycleAsync(CancellationToken cancellationToken)
    {
        await _runLock.WaitAsync(cancellationToken);
        try
        {
            status.MarkAttemptStarted();
            try
            {
                await RunOnceAsync(cancellationToken);
                status.MarkSuccess();
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // El mensaje de log original ("probablemente sin conexión a internet") asumía
                // la causa más común, pero también puede ser un error real (401 por clave mal
                // pegada, 404 por URL mal escrita, RLS, etc.) — SupabaseSyncStatus.LastError
                // guarda el mensaje completo de la excepción para que se vea en la app sin
                // tener que ir a buscar el archivo de log.
                logger.LogWarning(ex,
                    "Ciclo de sincronización con Supabase falló (puede ser falta de internet o un " +
                    "error real de configuración/credenciales). Se reintenta en el siguiente ciclo, " +
                    "nada se pierde localmente.");
                status.MarkFailure(ex.Message);
                return false;
            }
        }
        finally
        {
            _runLock.Release();
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var restClient = services.GetRequiredService<SupabaseRestClient>();

        // Cada tabla se sube de forma AISLADA (try/catch propio dentro de PushFullTableAsync
        // y PushAttendancesIncrementalAsync) — antes, un fallo en cualquier tabla (p. ej.
        // "employees" por un dato problemático) lanzaba una excepción que abortaba TODO el
        // resto del ciclo, dejando congeladas para siempre las tablas que venían después en
        // este mismo orden (devices, employee_device_mappings, app_users, attendances),
        // aunque su propio push hubiera funcionado perfectamente. Ahora un fallo puntual en
        // una tabla se registra y se reporta (ver failures más abajo), pero no le quita la
        // oportunidad a las demás de sincronizar en el mismo ciclo.
        var failures = new List<string>();

        await PushFullTableAsync(restClient, "branches",
            (await services.GetRequiredService<IBranchRepository>().ListAsync(cancellationToken))
                .Select(BranchDto.FromDomain).ToList(), failures, cancellationToken);

        await PushFullTableAsync(restClient, "employees",
            (await services.GetRequiredService<IEmployeeRepository>().ListAsync(cancellationToken))
                .Select(EmployeeDto.FromDomain).ToList(), failures, cancellationToken);

        await PushFullTableAsync(restClient, "devices",
            (await services.GetRequiredService<IDeviceRepository>().ListAsync(cancellationToken))
                .Select(DeviceDto.FromDomain).ToList(), failures, cancellationToken);

        await PushFullTableAsync(restClient, "employee_device_mappings",
            (await services.GetRequiredService<IEmployeeDeviceMappingRepository>().ListAsync(cancellationToken))
                .Select(EmployeeDeviceMappingDto.FromDomain).ToList(), failures, cancellationToken);

        await PushFullTableAsync(restClient, "app_users",
            (await services.GetRequiredService<IUserRepository>().ListAsync(cancellationToken))
                .Select(AppUserDto.FromDomain).ToList(), failures, cancellationToken);

        await PushFullTableAsync(restClient, "payroll_deductions",
            (await services.GetRequiredService<IPayrollDeductionRepository>().ListAsync(cancellationToken))
                .Select(PayrollDeductionDto.FromDomain).ToList(), failures, cancellationToken);

        await PushAttendancesIncrementalAsync(restClient, services, failures, cancellationToken);

        if (failures.Count > 0)
        {
            // Se lanza al final (no antes) para que RunCycleAsync siga marcando el ciclo
            // como fallido en SupabaseSyncStatus (el punto rojo/tooltip sigue siendo
            // confiable como señal de "algo está mal"), pero solo DESPUÉS de que todas las
            // tablas ya tuvieron su oportunidad de subir.
            throw new InvalidOperationException(string.Join(" | ", failures));
        }
    }

    private async Task PushFullTableAsync<T>(
        SupabaseRestClient restClient, string table, IReadOnlyCollection<T> rows, List<string> failures,
        CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        try
        {
            await restClient.UpsertBatchAsync(table, rows, cancellationToken);
            logger.LogDebug("Sincronizadas {Count} fila(s) de '{Table}'.", rows.Count, table);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo sincronizar la tabla '{Table}' — se continúa con las demás.", table);
            failures.Add($"{table}: {ex.Message}");
        }
    }

    private async Task PushAttendancesIncrementalAsync(
        SupabaseRestClient restClient, IServiceProvider services, List<string> failures, CancellationToken cancellationToken)
    {
        var attendanceRepository = services.GetRequiredService<IAttendanceRepository>();
        var cursorStore = services.GetRequiredService<ISyncCursorStore>();
        var cursor = await cursorStore.GetCursorAsync(AttendanceCursorKey, cancellationToken);
        var totalPushed = 0;

        try
        {
            // Se drena en lotes dentro del mismo ciclo (no solo un lote por tick) para que,
            // tras una temporada sin internet, la cola pendiente no tarde horas en ponerse
            // al día — cada vuelta reevalúa el cursor ya avanzado, así que nunca reprocesa
            // lo recién enviado.
            while (true)
            {
                var pending = await attendanceRepository.ListChangedSinceAsync(cursor, AttendanceBatchSize, cancellationToken);
                if (pending.Count == 0)
                {
                    break;
                }

                var dtos = pending.Select(AttendanceDto.FromDomain).ToList();
                await restClient.UpsertBatchAsync("attendances", dtos, cancellationToken);

                cursor = pending[^1].UpdatedAtUtc;
                await cursorStore.SetCursorAsync(AttendanceCursorKey, cursor, cancellationToken);
                totalPushed += pending.Count;

                if (pending.Count < AttendanceBatchSize)
                {
                    break; // ya no queda nada pendiente
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El cursor solo avanza tras un upsert exitoso (ver arriba), así que un fallo a
            // mitad de un lote no pierde ni reordena nada: el siguiente ciclo retoma desde
            // el último cursor confirmado.
            logger.LogWarning(ex, "No se pudo sincronizar asistencias — se continúa con las demás tablas.");
            failures.Add($"attendances: {ex.Message}");
        }

        if (totalPushed > 0)
        {
            logger.LogDebug("Sincronizadas {Count} marcación(es) de asistencia.", totalPushed);
        }
    }
}
