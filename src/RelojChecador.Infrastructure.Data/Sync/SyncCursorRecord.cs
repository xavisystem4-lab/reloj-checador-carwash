namespace RelojChecador.Infrastructure.Data.Sync;

/// <summary>
/// Fila de persistencia para <see cref="ISyncCursorStore"/> — deliberadamente NO es una
/// entidad de Domain (no tiene reglas de negocio, es un detalle técnico de la
/// sincronización), por eso vive directamente en Infrastructure.Data en vez de en
/// RelojChecador.Domain.
/// </summary>
public sealed class SyncCursorRecord
{
    public string EntityType { get; set; } = null!;
    public DateTime CursorUtc { get; set; }
}
