using RelojChecador.Application.Attendances;
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
///
/// <c>CalculateWeek</c> además clasifica cada uno de los 7 días de la semana en
/// <see cref="DailyAttendanceEntry.Status"/>: si hubo al menos una marcación ese día es
/// <c>Worked</c> (aunque el cálculo de horas haya dado advertencias — el empleado sí se
/// presentó); si NO hubo ninguna marcación, el primer día así (en orden lunes→domingo) es
/// <c>RestDay</c> (un empleado descansa 1 día por semana) y cualquier día sin marcación
/// posterior a ese es <c>Absence</c>; los días de la semana en curso que todavía no
/// ocurren (hoy o futuro) son <c>Pending</c> — nunca se marca una falta antes de tiempo.
/// Esta clasificación es puramente informativa: el sueldo semanal se sigue pagando
/// completo (ver <see cref="WeeklyPayrollSummary"/>), es el administrador quien decide
/// ajustar manualmente por horas extra, faltas o cualquier otro motivo.
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
        Employee employee, DateOnly weekStart, IReadOnlyList<Attendance> weekAttendances, DateOnly? asOfDate = null)
    {
        // "Hoy", para no marcar como descanso/falta un día de la semana en curso que
        // todavía no ha ocurrido — parámetro explícito (en vez de leer DateTime.Now aquí
        // dentro) para que la clasificación siga siendo pura y 100% probable con xUnit
        // sin depender del reloj del sistema (ver comentario de clase).
        var today = asOfDate ?? DateOnly.FromDateTime(DateTime.Now);

        var warnings = new List<string>();
        var totalRegular = TimeSpan.Zero;
        var totalOvertime = TimeSpan.Zero;

        // Se agrupa por fecha calendario del propio TimestampUtc — igual criterio que el
        // resto de la app (ver AttendanceViewModel): no hay conversión real de zona
        // horaria, se asume que el negocio opera en una sola.
        var byDay = weekAttendances
            .GroupBy(a => DateOnly.FromDateTime(a.TimestampUtc))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Attendance>)g.ToList());

        var dailyBreakdown = new List<DailyAttendanceEntry>();
        var restDayTaken = false;

        for (var offset = 0; offset < 7; offset++)
        {
            var date = weekStart.AddDays(offset);

            if (byDay.TryGetValue(date, out var dayAttendances))
            {
                var daySummary = CalculateDay(date, dayAttendances);
                totalRegular += daySummary.RegularTime;
                totalOvertime += daySummary.OvertimeTime;
                warnings.AddRange(daySummary.Warnings.Select(w => $"{date:dd/MM}: {w}"));
                var firstPunchUtc = dayAttendances.Min(a => a.TimestampUtc);
                var color = PunctualityClassifier.Classify(
                    DayAttendanceStatus.Worked, employee.HasSpecialSchedule, employee.ScheduledStartTime, firstPunchUtc);
                dailyBreakdown.Add(new DailyAttendanceEntry(
                    date, DayAttendanceStatus.Worked, daySummary.RegularTime, daySummary.OvertimeTime, daySummary.Warnings, color));
                continue;
            }

            // Sin ninguna marcación ese día. Un día de hoy en adelante todavía puede
            // recibir una marcación más tarde — no se clasifica como descanso ni falta.
            if (date >= today)
            {
                var pendingColor = PunctualityClassifier.Classify(
                    DayAttendanceStatus.Pending, employee.HasSpecialSchedule, employee.ScheduledStartTime, null);
                dailyBreakdown.Add(new DailyAttendanceEntry(date, DayAttendanceStatus.Pending, TimeSpan.Zero, TimeSpan.Zero, [], pendingColor));
                continue;
            }

            var status = restDayTaken ? DayAttendanceStatus.Absence : DayAttendanceStatus.RestDay;
            restDayTaken = true;
            var statusColor = PunctualityClassifier.Classify(
                status, employee.HasSpecialSchedule, employee.ScheduledStartTime, null);
            dailyBreakdown.Add(new DailyAttendanceEntry(date, status, TimeSpan.Zero, TimeSpan.Zero, [], statusColor));
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

        // Sueldo pendiente de captura (null): NUNCA se trata como $0 — se advierte
        // explícitamente y el total refleja solo lo que sí se conoce (horas extra), en
        // vez de disfrazar un dato faltante como si fuera un pago real.
        decimal totalPay;
        if (employee.WeeklySalary is null)
        {
            warnings.Add("Sueldo semanal pendiente de captura — no se incluyó en el total.");
            totalPay = overtimePay;
        }
        else
        {
            totalPay = employee.WeeklySalary.Value + overtimePay;
        }

        return new WeeklyPayrollSummary(
            employee.Id, weekStart, weekStart.AddDays(6), totalRegular, totalOvertime,
            employee.WeeklySalary, employee.OvertimeHourlyRate, overtimePay, totalPay, warnings, dailyBreakdown);
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
                    // DOBLE CHECADA — decisión explícita del usuario (18/09/2026, con el caso real
                    // de Adali: 07:53 Entrada, 15:29 Entrada, 15:53 Salida): se conserva la PRIMERA
                    // entrada y se ignora la repetida. Antes se conservaba la más reciente y ese
                    // día contaba 0:24 h en vez de 8:00 (y otros días 1:53 h) — la primera checada
                    // es la llegada real; la segunda suele ser un toque repetido cerca de la
                    // salida (el reloj F22/ID no tiene botones Entrada/Salida, el tipo lo asigna
                    // ShiftPunchTypeClassifier con reglas de tiempo y no puede saberlo). Igual que
                    // el reporte web: primera checada del día = entrada.
                    warnings.Add(
                        $"Dos marcaciones de inicio de {label} seguidas sin su cierre (se tomó la de las {openAt:HH:mm} y se ignoró la de las {attendance.TimestampUtc:HH:mm}).");
                    continue;
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
