using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Bucle periódico dedicado a consultar public.sync_requests (ver
/// RemoteSyncRequestCoordinator.PollForPendingRequestAsync) — mismo intervalo
/// (SupabaseSyncOptions.IntervalSeconds) y misma forma de bucle que
/// SupabaseSyncBackgroundService.ExecuteAsync, pero un servicio propio y no una rama más
/// dentro de aquel: son dos responsabilidades independientes (uno EMPUJA datos locales
/// hacia la nube, este otro LEE solicitudes desde la nube) que no tienen por qué compartir
/// ciclo de vida ni bloquearse mutuamente.
/// </summary>
public sealed class RemoteSyncRequestPollingService(
    RemoteSyncRequestCoordinator coordinator,
    SupabaseSyncOptions options,
    ILogger<RemoteSyncRequestPollingService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured)
        {
            // Mismo criterio que SupabaseSyncBackgroundService: sin credenciales de
            // Supabase configuradas, no hay nada que consultar — la app sigue funcionando
            // 100% local.
            return;
        }

        logger.LogInformation(
            "Consulta de solicitudes de sincronización remota activa. Intervalo: {IntervalSeconds}s.",
            options.IntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await coordinator.PollForPendingRequestAsync(stoppingToken);

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
}
