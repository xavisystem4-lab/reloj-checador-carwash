namespace RelojChecador.Application.Common;

/// <summary>
/// Confirma en una sola transacción los cambios hechos a través de uno o más
/// repositorios durante un caso de uso. Los repositorios solo rastrean/agregan
/// entidades; nada se persiste hasta llamar a <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Deshace lo que todavía no se guardó (altas, cambios y bajas pendientes). La app
    /// comparte UN solo contexto durante toda la sesión: sin esto, un guardado fallido deja
    /// sus cambios pendientes y hace fallar todos los SaveChangesAsync siguientes.</summary>
    void DiscardPendingChanges();
}
