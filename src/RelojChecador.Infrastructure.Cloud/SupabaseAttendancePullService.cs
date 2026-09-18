using Microsoft.Extensions.DependencyInjection;
using RelojChecador.Infrastructure.Cloud.Dtos;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Lectura (nunca escritura) de las marcaciones que ya están en Supabase para un rango de
/// fechas — usado por el reporte semanal (PayrollViewModel) para completar la base local
/// con marcaciones que esta PC no descargó del reloj (p. ej. otra PC las bajó, o esta
/// instalación es nueva). Deliberadamente no sabe nada de SQLite: solo devuelve DTOs, quien
/// los consuma decide cómo guardarlos (mismo reparto de capas que
/// <see cref="RemoteSyncRequestCoordinator"/>).
///
/// Seguro de inyectar sin Supabase configurado: <see cref="IsConfigured"/> es false y
/// <see cref="FetchAsync"/> devuelve vacío en vez de lanzar.
/// </summary>
public sealed class SupabaseAttendancePullService(IServiceScopeFactory scopeFactory, SupabaseSyncOptions options)
{
    // PostgREST recorta cada respuesta a 1000 filas por defecto (max-rows de Supabase).
    private const int PageSize = 1000;

    public bool IsConfigured => options.IsConfigured;

    /// <summary>Marcaciones con TimestampUtc en [fromUtc, toUtc) — mismo criterio de "hora
    /// de pared etiquetada como UTC" que usa el resto de la app (ver
    /// PayrollViewModel.LoadAsync). Lanza si Supabase no responde; quien llama decide si eso
    /// es fatal (para el reporte no lo es, se muestra lo que ya hay local).</summary>
    public async Task<IReadOnlyList<AttendanceDto>> FetchAsync(
        DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        if (!options.IsConfigured)
        {
            return [];
        }

        using var scope = scopeFactory.CreateScope();
        var restClient = scope.ServiceProvider.GetRequiredService<SupabaseRestClient>();

        var from = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc).ToString("O");
        var to = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc).ToString("O");

        var all = new List<AttendanceDto>();
        for (var offset = 0; ; offset += PageSize)
        {
            var query = $"timestamp_utc=gte.{from}&timestamp_utc=lt.{to}&order=timestamp_utc.asc,id.asc&limit={PageSize}&offset={offset}";
            var page = await restClient.GetAsync<AttendanceDto>("attendances", query, cancellationToken);
            all.AddRange(page);
            if (page.Count < PageSize)
            {
                return all;
            }
        }
    }
}
