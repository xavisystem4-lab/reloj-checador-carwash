namespace RelojChecador.Application.Payroll;

/// <summary>Resultado de <see cref="WorkedHoursCalculator.CalculateWeek"/> para un
/// empleado en una semana — insumo de nómina sin ningún cálculo fiscal (ISR/IMSS): solo
/// suma el sueldo semanal fijo del empleado más el pago de horas extra (tarifa fija en
/// pesos capturada por el usuario, NUNCA la regla de Ley Federal del Trabajo). Ver
/// comentario de clase de <see cref="RelojChecador.Domain.Employees.Employee"/>.</summary>
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
    IReadOnlyList<string> Warnings);
