namespace RelojChecador.Application.Common;

/// <summary>
/// Confirma en una sola transacción los cambios hechos a través de uno o más
/// repositorios durante un caso de uso. Los repositorios solo rastrean/agregan
/// entidades; nada se persiste hasta llamar a <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
