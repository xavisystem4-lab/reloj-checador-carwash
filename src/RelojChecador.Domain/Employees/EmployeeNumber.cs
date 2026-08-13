using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Employees;

/// <summary>
/// Número de empleado: la clave de negocio usada para conciliar identidad entre
/// sucursales y dispositivos. NO es necesariamente igual al PIN interno que cada reloj
/// checador le asigna al empleado — esa relación se modela explícitamente en
/// <see cref="RelojChecador.Domain.EmployeeDeviceMappings.EmployeeDeviceMapping"/>.
/// </summary>
public sealed class EmployeeNumber : ValueObject
{
    private const int MaxLength = 20;

    public string Value { get; }

    private EmployeeNumber(string value)
    {
        Value = value;
    }

    public static EmployeeNumber Create(string value)
    {
        Guard.AgainstNullOrWhiteSpace(value, nameof(value));
        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new DomainException($"El número de empleado no puede exceder {MaxLength} caracteres.");
        }

        return new EmployeeNumber(trimmed);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}
