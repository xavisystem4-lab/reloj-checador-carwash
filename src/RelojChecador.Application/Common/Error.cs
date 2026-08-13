namespace RelojChecador.Application.Common;

/// <summary>
/// Error operacional esperado (red caída, dispositivo inalcanzable, autenticación
/// rechazada) — distinto de <c>RelojChecador.Domain.Common.DomainException</c>, que
/// representa violaciones de invariantes de negocio. Un <see cref="Error"/> es un valor,
/// no una excepción: se espera que ocurra y el llamador decide cómo reaccionar.
/// </summary>
public readonly record struct Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Unexpected(string message) => new("General.Unexpected", message);
}
