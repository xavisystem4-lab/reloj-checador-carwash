namespace RelojChecador.Domain.Common;

/// <summary>
/// Entidad con metadatos de auditoría y un token de concurrencia optimista. El
/// <see cref="ConcurrencyToken"/> se regenera en cada modificación y sirve tanto para
/// EF Core (control de concurrencia) como para el motor de sincronización con Supabase
/// (detección de conflictos entre el cambio local y el remoto).
/// </summary>
public abstract class AuditableEntity : Entity
{
    public DateTime CreatedAtUtc { get; protected set; }
    public DateTime UpdatedAtUtc { get; protected set; }
    public Guid ConcurrencyToken { get; protected set; }

    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid id) : base(id)
    {
    }

    /// <summary>Debe llamarse al final de cualquier método que modifique el estado de la entidad.</summary>
    protected void Touch()
    {
        UpdatedAtUtc = DateTime.UtcNow;
        ConcurrencyToken = Guid.NewGuid();
    }

    /// <summary>Inicializa los campos de auditoría al crear la entidad por primera vez.</summary>
    protected void InitializeAuditFields()
    {
        var now = DateTime.UtcNow;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        ConcurrencyToken = Guid.NewGuid();
    }
}
