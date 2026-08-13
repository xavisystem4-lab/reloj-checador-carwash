namespace RelojChecador.Domain.Common;

/// <summary>
/// Validaciones de precondición reutilizables para los factory methods de las entidades.
/// Lanzan <see cref="DomainException"/> — nunca excepciones genéricas de .NET — para que
/// las capas superiores puedan distinguir violaciones de reglas de negocio de otros errores.
/// </summary>
public static class Guard
{
    public static void AgainstNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"El valor de '{paramName}' es requerido.");
        }
    }

    public static void AgainstEmptyGuid(Guid value, string paramName)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"El valor de '{paramName}' no puede ser un identificador vacío.");
        }
    }

    public static void AgainstOutOfRange(int value, int minInclusive, int maxInclusive, string paramName)
    {
        if (value < minInclusive || value > maxInclusive)
        {
            throw new DomainException(
                $"El valor de '{paramName}' ({value}) debe estar entre {minInclusive} y {maxInclusive}.");
        }
    }
}
