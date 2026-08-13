namespace RelojChecador.Domain.Common;

/// <summary>
/// Se lanza cuando se viola una regla o invariante del dominio (p. ej. crear un empleado
/// sin sucursal, o un dispositivo con un puerto TCP fuera de rango). No se usa para errores
/// operativos esperados (red caída, dispositivo inalcanzable) — esos se modelan con el
/// patrón Result en la capa de Application.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
