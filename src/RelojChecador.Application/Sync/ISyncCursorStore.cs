namespace RelojChecador.Application.Sync;

/// <summary>
/// Recuerda, por tipo de entidad, hasta qué punto en el tiempo ya se sincronizó con
/// Supabase — para que el motor de sincronización (RelojChecador.Infrastructure.Cloud)
/// no tenga que reenviar todo el historial en cada ciclo, solo lo nuevo. Es un detalle
/// técnico de la sincronización, no una entidad de negocio — por eso vive en su propia
/// interfaz pequeña en vez de mezclarse con los repositorios del dominio.
/// </summary>
public interface ISyncCursorStore
{
    /// <returns><see cref="DateTime.MinValue"/> si nunca se ha sincronizado ese tipo de entidad.</returns>
    Task<DateTime> GetCursorAsync(string entityType, CancellationToken cancellationToken = default);

    Task SetCursorAsync(string entityType, DateTime valueUtc, CancellationToken cancellationToken = default);
}
