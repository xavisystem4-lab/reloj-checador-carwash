using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RelojChecador.Infrastructure.Cloud.Dtos;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>Datos mínimos de una solicitud de sincronización remota recién detectada —
/// exactamente lo que necesita quien la procese (ver DevicesViewModel.SyncRequested).</summary>
public sealed record RemoteSyncRequest(Guid Id, DateTime RequestedAtUtc, string? RequestedByEmail);

/// <summary>
/// Puente entre el Dashboard ("Actualizar asistencias") y el sistema local: detecta
/// solicitudes pendientes en public.sync_requests (ver
/// supabase/migrations/20260814060000_add_sync_requests.sql) y las reporta vía evento —
/// mismo patrón ya usado por IAttendanceDeviceAdapter.AttendancePunchReceived. Deliberadamente
/// NO sabe nada de dispositivos ni de SQLite: quien procese el evento (DevicesViewModel, en
/// la capa WPF, que ya posee toda la lógica de conectar/descargar/sincronizar) es quien
/// reporta el resultado de vuelta con CompleteAsync. Mantiene la separación de capas:
/// Infrastructure.Cloud nunca toca IAttendanceDeviceAdapter.
///
/// Singleton — un único guardia de "solicitud activa" para toda la app, coherente con que
/// hoy solo hay un dispositivo real en producción (ver el índice único parcial en la
/// migración, que impone la misma regla del lado de la base de datos).
///
/// Seguro de inyectar en cualquier lado (incluida DevicesViewModel) aunque Supabase no esté
/// configurado en esta instalación: cada método revisa SupabaseSyncOptions.IsConfigured
/// primero y no hace nada si no — mismo criterio que SupabaseSyncBackgroundService.
/// </summary>
public sealed class RemoteSyncRequestCoordinator(
    IServiceScopeFactory scopeFactory,
    SupabaseSyncOptions options,
    ILogger<RemoteSyncRequestCoordinator> logger)
{
    /// <summary>Ventana de gracia antes de considerar abandonada una solicitud que quedó
    /// "in_progress" sin completarse — cubre un caso real reportado por el usuario: la app
    /// se cerró a la mitad del proceso (crash — ver el fix de v1.17.2) sin alcanzar a
    /// llamar CompleteAsync, y esa fila quedó "Sincronizando…" para siempre en el
    /// Dashboard, porque PollForPendingRequestAsync solo buscaba status=eq.pending y
    /// _activeRequestId es una guardia en memoria que se pierde al reiniciar la app —
    /// nada volvía a recogerla jamás. 2 minutos es generoso frente a lo que tarda
    /// conectar+descargar+sincronizar en la práctica (segundos, no minutos).</summary>
    private static readonly TimeSpan StaleInProgressThreshold = TimeSpan.FromMinutes(2);

    private readonly object _lock = new();
    private Guid? _activeRequestId;

    /// <summary>Se dispara en el hilo del RemoteSyncRequestPollingService, NO en el de UI —
    /// quien escuche debe hacer su propio marshaling (Dispatcher.Invoke) antes de tocar
    /// controles, igual que con AttendancePunchReceived.</summary>
    public event EventHandler<RemoteSyncRequest>? SyncRequested;

    /// <summary>Llamado periódicamente por RemoteSyncRequestPollingService. Trae la
    /// solicitud "pending" más antigua (si hay alguna), la marca "in_progress" de
    /// inmediato — así, si por error hubiera más de una instancia de la app corriendo, la
    /// otra ve el filtro status=eq.pending vacío en su próximo ciclo y no duplica el
    /// trabajo — y recién entonces dispara el evento.</summary>
    public async Task PollForPendingRequestAsync(CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            return;
        }

        lock (_lock)
        {
            // Ya hay una solicitud en curso en ESTA instancia — no pedir otra hasta
            // completar la actual (guardia en memoria, complementa al índice único parcial
            // de la base, que protege contra otras instancias/pestañas).
            if (_activeRequestId is not null)
            {
                return;
            }
        }

        List<SyncRequestDto> pending;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var restClient = scope.ServiceProvider.GetRequiredService<SupabaseRestClient>();

            // Trae "pending" normales O "in_progress" abandonadas hace más de
            // StaleInProgressThreshold (ver su comentario) — así una solicitud huérfana por
            // un crash se recoge sola en el siguiente ciclo tras reiniciar la app, sin
            // depender de que alguien la reintente a mano desde el Dashboard.
            var staleThreshold = (DateTime.UtcNow - StaleInProgressThreshold).ToString("O");
            var filter = $"or=(status.eq.pending,and(status.eq.in_progress,started_at_utc.lt.{staleThreshold}))" +
                         "&order=requested_at_utc.asc&limit=1";
            pending = await restClient.GetAsync<SyncRequestDto>("sync_requests", filter, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Sin conexión a internet es un caso esperado (igual que el resto del motor de
            // sync) — se reintenta solo en el siguiente ciclo, no hace falta escalar esto
            // a SupabaseSyncStatus: es una preocupación aparte de "¿está subiendo mis
            // datos?", y una solicitud pendiente simplemente sigue pendiente.
            logger.LogDebug(ex, "No se pudo consultar solicitudes de sincronización remota pendientes.");
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        var request = pending[0];

        try
        {
            using var scope = scopeFactory.CreateScope();
            var restClient = scope.ServiceProvider.GetRequiredService<SupabaseRestClient>();
            await restClient.PatchAsync("sync_requests", $"id=eq.{request.Id}",
                new { status = "in_progress", started_at_utc = DateTime.UtcNow }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex,
                "No se pudo marcar la solicitud de sincronización remota {RequestId} como en curso — se reintenta en el siguiente ciclo.",
                request.Id);
            return;
        }

        lock (_lock)
        {
            _activeRequestId = request.Id;
        }

        logger.LogInformation("Solicitud de sincronización remota {RequestId} recibida — procesando.", request.Id);
        SyncRequested?.Invoke(this, new RemoteSyncRequest(request.Id, request.RequestedAtUtc, request.RequestedByEmail));
    }

    /// <summary>Reporta el resultado de procesar una solicitud — llamado por
    /// DevicesViewModel al terminar (con éxito o no) de conectar/descargar/sincronizar.
    /// Libera el guardia interno en cualquier caso (incluso si el PATCH final falla), para
    /// no dejar la app permanentemente atascada sin poder recoger la siguiente
    /// solicitud.</summary>
    public async Task CompleteAsync(Guid requestId, bool success, string message, CancellationToken cancellationToken)
    {
        try
        {
            if (!options.IsConfigured)
            {
                // No debería pasar en la práctica (solo se llega aquí tras un SyncRequested,
                // que ya implica IsConfigured), pero defensivo: nunca lanzar por esto.
                return;
            }

            using var scope = scopeFactory.CreateScope();
            var restClient = scope.ServiceProvider.GetRequiredService<SupabaseRestClient>();

            object body = success
                ? new { status = "completed", completed_at_utc = DateTime.UtcNow, result_summary = message }
                : new { status = "failed", completed_at_utc = DateTime.UtcNow, error_message = message };

            await restClient.PatchAsync("sync_requests", $"id=eq.{requestId}", body, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "No se pudo reportar el resultado de la solicitud de sincronización remota {RequestId}.", requestId);
        }
        finally
        {
            lock (_lock)
            {
                if (_activeRequestId == requestId)
                {
                    _activeRequestId = null;
                }
            }
        }
    }
}
