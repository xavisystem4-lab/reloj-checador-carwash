using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Payroll;

/// <summary>
/// Calcula horas trabajadas y el insumo de nómina (sueldo semanal + pago de horas extra,
/// SIN ningún cálculo fiscal — ver comentario de clase de Employee) a partir de las
/// marcaciones de <c>Attendance.PunchType</c>: 0=Entrada, 1=Salida, 2=Salida a descanso,
/// 3=Entrada de descanso, 4=Entrada tiempo extra, 5=Salida tiempo extra (ver
/// PunchTypeToTextConverter en RelojChecador.WPF).
///
/// ADVERTENCIA REAL que hay que trasladar a quien use este resultado: los valores 2 y 3
/// (descansos) nunca se han confirmado contra el hardware real (F22/ID) — solo 0, 1, 4 y 5
/// se han visto en descargas reales (ver mismo comentario del converter). El cálculo de
/// descansos es, por tanto, especulativo hasta que se confirme.
///
/// Diseño deliberadamente defensivo: nunca "inventa" el cierre de un turno abierto ni
/// asume el emparejamiento correcto si algo no cuadra (dos entradas seguidas sin salida,
/// una salida sin su entrada, un turno que quedó abierto todo el día). Esos casos se
/// reportan en <c>Warnings</c> en vez de sumarse a ciegas — es lógica pura sin
/// dependencias de infraestructura, para poder probarla exhaustivamente con xUnit.
/// </summary>
public static class WorkedHoursCalculator
{
    private const int PunchIn = 0;
    private const int PunchOut = 1;
    private const int BreakOut = 2;
    private const int BreakIn = 3;
    private const int OvertimeIn = 4;
    private const int OvertimeOut = 5;

    public static DailyWorkSummary CalculateDay(DateOnly date, IReadOnlyList<Attendance> dayAttendances)
    {
        var warnings = new List<string>();
        var sorted = dayAttendances.OrderBy(a => a.TimestampUtc).ToList();

        var regular = PairAndSum(sorted, PunchIn, PunchOut, "turno normal", warnings);
        var breakTime = PairAndSum(sorted, BreakOut, BreakIn, "descanso", warnings);
        var overtime = PairAndSum(sorted, OvertimeIn, OvertimeOut, "tiempo extra", warnings);

        // El descanso nunca resta más de lo que se trabajó — un dato inconsistente (p. ej.
        // un descanso mal marcado que "dura" más que el propio turno) se reporta como
        // advertencia en vez de dejar tiempo negativo.
        var netRegular = regular - breakTime;
        if (netRegular < TimeSpan.Zero)
        {
            warnings.Add(
                $"El tiempo de descanso ({FormatHours(breakTime)}) es mayor que el turno normal ({FormatHours(regular)}) — se registró 0h en vez de un valor negativo.");
            netRegular = TimeSpan.Zero;
        }

        return new DailyWorkSummary(date, netRegular, overtime, warnings);
    }

    public static WeeklyPayrollSummary CalculateWeek(
        Employee employee, DateOnly weekStart, IReadOnlyList<Attendance> weekAttendances)
    {
        var warnings = new List<string>();
        var totalRegular = TimeSpan.Zero;
        var totalOvertime = TimeSpan.Zero;

        // Se agrupa por fecha calendario del propio TimestampUtc — igual criterio que el
        // resto de la app (ver AttendanceViewModel): no hay conversión real de zona
        // horaria, se asume que el negocio opera en una sola.
        var byDay = weekAttendances.GroupBy(a => DateOnly.FromDateTime(a.TimestampUtc)).OrderBy(g => g.Key);
        foreach (var dayGroup in byDay)
        {
            var daySummary = CalculateDay(dayGroup.Key, dayGroup.ToList());
            totalRegular += daySummary.RegularTime;
            totalOvertime += daySummary.OvertimeTime;
            warnings.AddRange(daySummary.Warnings.Select(w => $"{dayGroup.Key:dd/MM}: {w}"));
        }

        var overtimePay = 0m;
        if (totalOvertime > TimeSpan.Zero)
        {
            if (employee.OvertimeHourlyRate is null)
            {
                warnings.Add(
                    $"Hubo {FormatHours(totalOvertime)} de tiempo extra pero el empleado no tiene tarifa de hora extra capturada — no se calculó su pago.");
            }
            else
            {
                overtimePay = (decimal)totalOvertime.TotalHours * employee.OvertimeHourlyRate.Value;
            }
        }

        var totalPay = employee.WeeklySalary + overtimePay;

        return new WeeklyPayrollSummary(
            employee.Id, weekStart, weekStart.AddDays(6), totalRegular, totalOvertime,
            employee.WeeklySalary, employee.OvertimeHourlyRate, overtimePay, totalPay, warnings);
    }

    /// <summary>Empareja cronológicamente cada marcación "abre" (openType) con la
    /// siguiente "cierra" (closeType) y suma la diferencia. Cualquier desbalance (dos
    /// aperturas seguidas, un cierre sin apertura, una apertura que nunca cierra) se
    /// reporta en <paramref name="warnings"/> — nunca se inventa la pareja faltante.</summary>
    private static TimeSpan PairAndSum(
        IReadOnlyList<Attendance> sortedDayAttendances, int openType, int closeType, string label, List<string> warnings)
    {
        var total = TimeSpan.Zero;
        DateTime? openAt = null;

        foreach (var attendance in sortedDayAttendances)
        {
            if (attendance.PunchType == openType)
            {
                if (openAt is not null)
                {
                    warnings.Add(
                        $"Dos marcaciones de inicio de {label} seguidas sin su cierre (la de las {openAt:HH:mm} se ignoró).");
                }
                openAt = attendance.TimestampUtc;
            }
            else if (attendance.PunchType == closeType)
            {
                if (openAt is null)
                {
                    warnings.Add($"Marcación de cierre de {label} a las {attendance.TimestampUtc:HH:mm} sin su inicio correspondiente — se ignoró.");
                    continue;
                }

                total += attendance.TimestampUtc - openAt.Value;
                openAt = null;
            }
        }

        if (openAt is not null)
        {
            warnings.Add($"Quedó un {label} sin cerrar (inició a las {openAt:HH:mm}) — ese tramo no se contó.");
        }

        return total;
    }

    private static string FormatHours(TimeSpan span) => $"{(int)span.TotalHours}h {span.Minutes}m";
}
