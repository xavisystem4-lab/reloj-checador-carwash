using RelojChecador.Domain.Attendances;

namespace RelojChecador.Application.Attendances;

/// <summary>Un turno que quedó abierto (Entrada sin Salida) y que ya alcanzó su hora de
/// corte — listo para que quien orqueste la persistencia (ver
/// RelojChecador.WPF.Services.AttendanceAutoCloseBackgroundService) genere la Salida real.
/// <paramref name="OpenEntrada"/> se conserva completa (no solo su Id) para poder reusar su
/// mismo DeviceId/DeviceUserPin/BranchId/EmployeeId al construir la Salida — así la nueva
/// marcación empareja con la Entrada que cierra, en vez de con un dispositivo/PIN
/// arbitrario.</summary>
public sealed record PendingAutoClose(Attendance OpenEntrada, DateTime CutoffUtc);

/// <summary>
/// Detecta turnos que quedaron abiertos (una Entrada sin su Salida) y decide si ya deben
/// cerrarse solos — pedido explícito del usuario: "si el empleado no checa a su hora de
/// salida esta se marca automáticamente para que no sigan corriendo las horas". Lógica
/// pura, sin dependencias de infraestructura (mismo criterio que WorkedHoursCalculator y
/// ShiftPunchTypeClassifier), para poder probarla exhaustivamente con xUnit.
///
/// Solo se activa para empleados CON horario de salida capturado
/// (<see cref="RelojChecador.Domain.Employees.Employee.ScheduledEndTime"/>): nunca inventa
/// una hora de corte sin ese dato real — sin horario, el turno se queda abierto tal cual,
/// igual que siempre (ver comentario de clase de WorkedHoursCalculator: "nunca inventa el
/// cierre de un turno abierto"). Esta clase es la única excepción deliberada a esa regla,
/// y solo cuando SÍ hay un horario capturado contra el cual comparar.
///
/// Un turno se considera abierto si, dentro de un mismo día calendario, la ÚLTIMA
/// marcación es una Entrada sin una Salida posterior ese mismo día — mismo criterio de "un
/// día nunca hereda el turno del día anterior" que <see cref="ShiftPunchTypeClassifier"/>.
/// TimestampUtc pese al nombre es, en la práctica, la hora LOCAL del negocio (ver
/// comentario de esa misma clase) — por eso comparar directo contra ScheduledEndTime, sin
/// ninguna conversión de huso horario, es válido.
/// </summary>
public static class AttendanceAutoCloser
{
    private const int EntradaCode = ShiftPunchTypeClassifier.EntradaCode;
    private const int SalidaCode = ShiftPunchTypeClassifier.SalidaCode;

    /// <summary>Busca, dentro de TODAS las marcaciones de un solo empleado (cualquier
    /// orden), los turnos que se quedaron abiertos y que ya alcanzaron su hora de corte
    /// según <paramref name="nowUtc"/>. Nunca devuelve más de un turno pendiente por día
    /// calendario.</summary>
    public static IReadOnlyList<PendingAutoClose> FindShiftsToClose(
        IReadOnlyList<Attendance> employeeAttendances, TimeOnly? scheduledEndTime, DateTime nowUtc)
    {
        if (scheduledEndTime is not { } end)
        {
            return [];
        }

        var result = new List<PendingAutoClose>();
        var byDay = employeeAttendances.GroupBy(a => DateOnly.FromDateTime(a.TimestampUtc));
        foreach (var dayGroup in byDay)
        {
            Attendance? openEntrada = null;
            foreach (var punch in dayGroup.OrderBy(a => a.TimestampUtc))
            {
                if (punch.PunchType == SalidaCode)
                {
                    openEntrada = null;
                }
                else if (openEntrada is null)
                {
                    // PunchType null (marcación vieja sin clasificar) o EntradaCode (0) se
                    // tratan igual — mismo criterio conservador que ShiftPunchTypeClassifier.
                    openEntrada = punch;
                }
                // Si ya había un turno abierto y esta fila es OTRA entrada, el turno sigue
                // abierto desde la hora ORIGINAL — no se reinicia con cada checada de más
                // (mismo criterio que ShiftPunchTypeClassifier.Classify).
            }

            if (openEntrada is null)
            {
                continue;
            }

            if (DetermineAutoCloseUtc(openEntrada.TimestampUtc, end, nowUtc) is { } cutoff)
            {
                result.Add(new PendingAutoClose(openEntrada, cutoff));
            }
        }

        return result;
    }

    /// <summary>La hora UTC(-de-pared) exacta en la que debe cerrarse un turno que empezó
    /// en <paramref name="openEntradaUtc"/>, o null si <paramref name="nowUtc"/> todavía no
    /// ha alcanzado ese momento. Turno nocturno (la hora de salida programada "cae antes"
    /// que la de entrada en el reloj de 24h — p. ej. entrada 22:00, salida 06:00): el corte
    /// real es al día siguiente, no el mismo día a una hora ya pasada.</summary>
    public static DateTime? DetermineAutoCloseUtc(DateTime openEntradaUtc, TimeOnly scheduledEndTime, DateTime nowUtc)
    {
        var cutoffUtc = DateOnly.FromDateTime(openEntradaUtc).ToDateTime(scheduledEndTime, DateTimeKind.Utc);
        if (cutoffUtc <= openEntradaUtc)
        {
            cutoffUtc = cutoffUtc.AddDays(1);
        }

        return nowUtc >= cutoffUtc ? cutoffUtc : null;
    }
}
