namespace RelojChecador.Domain.Common;

/// <summary>
/// Base para toda entidad con identidad propia. La igualdad se basa en el Id, no en los
/// valores de sus campos (a diferencia de <see cref="ValueObject"/>).
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; }

    protected Entity()
    {
    }

    protected Entity(Guid id)
    {
        Guard.AgainstEmptyGuid(id, nameof(id));
        Id = id;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not Entity other || other.GetType() != GetType())
        {
            return false;
        }

        if (Id == Guid.Empty || other.Id == Guid.Empty)
        {
            return ReferenceEquals(this, other);
        }

        return Id == other.Id;
    }

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
