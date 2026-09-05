namespace RelojChecador.Application.Payroll;

/// <summary>Un día calendario dentro del desglose semanal de <see cref="WeeklyPayrollSummary.DailyBreakdown"/>.
/// Cuando <see cref="Status"/> es <see cref="DayAttendanceStatus.RestDay"/>, <see cref="DayAttendanceStatus.Absence"/>
/// o <see cref="DayAttendanceStatus.Pending"/>, <see cref="RegularTime"/> y <see cref="OvertimeTime"/> son siempre
/// cero — no hubo marcaciones que calcular ese día (ver <see cref="WorkedHoursCalculator"/>).</summary>
public sealed record DailyAttendanceEntry(
    DateOnly Date, DayAttendanceStatus Status, TimeSpan RegularTime, TimeSpan OvertimeTime, IReadOnlyList<string> Warnings);
