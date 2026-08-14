namespace RelojChecador.Application.Payroll;

/// <summary>
/// Único punto que decide "la semana laboral empieza en lunes" — el usuario pidió que la
/// semana sea lunes a domingo por ahora, pero que quede fácil de editar más adelante (sin
/// construir todavía una UI de configuración). Si el criterio cambia, este es el único
/// lugar del código que hay que tocar; nada más debe calcular el inicio de semana por su
/// cuenta.
/// </summary>
public static class WeekBoundary
{
    public static DateOnly GetWeekStart(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    public static DateOnly GetWeekEnd(DateOnly date) => GetWeekStart(date).AddDays(6);
}
