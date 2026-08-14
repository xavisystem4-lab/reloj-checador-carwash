namespace RelojChecador.Application.Payroll;

/// <summary>Resultado de <see cref="WorkedHoursCalculator.CalculateDay"/> para un solo
/// día: tiempo normal (ya con los descansos descontados) y tiempo extra, más cualquier
/// marcación que no se pudo emparejar (nunca se suma a ciegas — ver comentario de clase
/// de WorkedHoursCalculator).</summary>
public sealed record DailyWorkSummary(
    DateOnly Date, TimeSpan RegularTime, TimeSpan OvertimeTime, IReadOnlyList<string> Warnings);
