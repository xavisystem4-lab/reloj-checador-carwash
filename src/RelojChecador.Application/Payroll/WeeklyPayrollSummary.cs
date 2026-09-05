namespace RelojChecador.Application.Payroll;

/// <summary>Resultado de <see cref="WorkedHoursCalculator.CalculateWeek"/> para un
/// empleado en una semana — insumo de nómina sin ningún cálculo fiscal (ISR/IMSS): solo
/// suma el sueldo semanal fijo del empleado más el pago de horas extra (tarifa fija en
/// pesos capturada por el usuario, NUNCA la regla de Ley Federal del Trabajo). El sueldo
/// semanal se paga completo tal cual está capturado — el desglose por día
/// (<see cref="DailyBreakdown"/>) es solo informativo para que el administrador ajuste a
/// mano horas extra, faltas o cualquier otro cambio (decisión explícita del usuario, no
/// se descuenta nada automáticamente). Ver comentario de clase de
/// <see cref="RelojChecador.Domain.Employees.Employee"/>.</summary>
public sealed record WeeklyPayrollSummary(
    Guid EmployeeId,
    DateOnly WeekStart,
    DateOnly WeekEnd,
    TimeSpan TotalRegularTime,
    TimeSpan TotalOvertimeTime,
    decimal? WeeklySalary,
    decimal? OvertimeHourlyRate,
    decimal OvertimePay,
    decimal TotalPay,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<DailyAttendanceEntry> DailyBreakdown)
{
    /// <summary>El día que el cálculo tomó como descanso semanal (el primero, en orden
    /// lunes→domingo, sin ninguna marcación) — null si todavía no se puede determinar
    /// (semana en curso sin ningún día vencido sin checar) o si los 7 días se trabajaron.</summary>
    public DateOnly? RestDay => DailyBreakdown.FirstOrDefault(d => d.Status == DayAttendanceStatus.RestDay)?.Date;

    /// <summary>Días de la semana sin marcación más allá del descanso permitido — cada
    /// uno es una falta que el administrador debe revisar y ajustar manualmente.</summary>
    public int AbsenceCount => DailyBreakdown.Count(d => d.Status == DayAttendanceStatus.Absence);
}
