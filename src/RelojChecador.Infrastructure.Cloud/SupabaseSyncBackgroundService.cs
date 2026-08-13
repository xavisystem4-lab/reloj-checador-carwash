using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Identity;
using RelojChecador.Application.Sync;
using RelojChecador.Infrastructure.Cloud.Dtos;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Empuja periódicamente los cambios locales hacia Supabase — nunca al revés (v1 es
/// push-only: la app de escritorio es la única fuente de verdad, el Dashboard solo lee).
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

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured)
        {
            logger.LogInformation(
                "Sincronización con Supabase deshabilitada (falta Url o ServiceRoleKey en " +
                "%LocalAppData%\\RelojChecador\\appsettings.Local.json) — la app sigue funcionando " +
                "100% local, sin nube por ahora.");
            status.MarkDisabled();
            return;
        }

        logger.LogInformation(
            "Sincronización con Supabase activa. Intervalo: {IntervalSeconds}s.", options.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            status.MarkAttemptStarted();
            try
            {
                await RunOnceAsync(stoppingToken);
                status.MarkSuccess();
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
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var restClient = services.GetRequiredService<SupabaseRestClient>();

        await PushFullTableAsync(restClient, "branches",
            (await services.GetRequiredService<IBranchRepository>().ListAsync(cancellationToken))
                .Select(BranchDto.FromDomain).ToList(), cancellationToken);

        await PushFullTableAsync(restClient, "employees",
            (await services.GetRequiredService<IEmployeeRepository>().ListAsync(cancellationToken))
                .Select(EmployeeDto.FromDomain).ToList(), cancellationToken);

        await PushFullTableAsync(restClient, "devices",
            (await services.GetRequiredService<IDeviceRepository>().ListAsync(cancellationToken))
                .Select(DeviceDto.FromDomain).ToList(), cancellationToken);

        await PushFullTableAsync(restClient, "employee_device_mappings",
            (await services.GetRequiredService<IEmployeeDeviceMappingRepository>().ListAsync(cancellationToken))
                .Select(EmployeeDeviceMappingDto.FromDomain).ToList(), cancellationToken);

        await PushFullTableAsync(restClient, "app_users",
            (await services.GetRequiredService<IUserRepository>().ListAsync(cancellationToken))
                .Select(AppUserDto.FromDomain).ToList(), cancellationToken);

        await PushAttendancesIncrementalAsync(restClient, services, cancellationToken);
    }

    private async Task PushFullTableAsync<T>(
        SupabaseRestClient restClient, string table, IReadOnlyCollection<T> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        await restClient.UpsertBatchAsync(table, rows, cancellationToken);
        logger.LogDebug("Sincronizadas {Count} fila(s) de '{Table}'.", rows.Count, table);
    }

    private async Task PushAttendancesIncrementalAsync(
        SupabaseRestClient restClient, IServiceProvider services, CancellationToken cancellationToken)
    {
        var attendanceRepository = services.GetRequiredService<IAttendanceRepository>();
        var cursorStore = services.GetRequiredService<ISyncCursorStore>();
        var cursor = await cursorStore.GetCursorAsync(AttendanceCursorKey, cancellationToken);
        var totalPushed = 0;

        // Se drena en lotes dentro del mismo ciclo (no solo un lote por tick) para que,
        // tras una temporada sin internet, la cola pendiente no tarde horas en ponerse al
        // día — cada vuelta reevalúa el cursor ya avanzado, así que nunca reprocesa lo
        // recién enviado.
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

        if (totalPushed > 0)
        {
            logger.LogDebug("Sincronizadas {Count} marcación(es) de asistencia.", totalPushed);
        }
    }
}
