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
/// Se activa para CUALQUIER empleado elegible (ver
/// AttendanceAutoCloseBackgroundService: activo, sin horario especial), tenga o no
/// capturado <see cref="RelojChecador.Domain.Employees.Employee.ScheduledEndTime"/>:
/// - CON horario capturado: se cierra exacto a esa hora (ver
///   <see cref="DetermineAutoCloseUtc"/>, con soporte de turno nocturno).
/// - SIN horario capturado: se cierra a las <see cref="DefaultShiftDuration"/> (8 horas)
///   desde que entró — regla general dada explícitamente por el usuario ("el horario de
///   todos son 8 horas"), MISMO número que ya usa el tope puramente visual del Dashboard
///   web (dashboard/app.js, capOpenUntilIso) para no tener dos respuestas distintas al
///   mismo problema. La diferencia es que aquí SÍ se persiste como marcación real —el tope
///   del Dashboard web es solo de pantalla, nunca escribe nada— así que una vez que este
///   servicio corre, ambos coinciden en el mismo resultado.
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

    /// <summary>8 horas — mismo número que dashboard/app.js (capOpenUntilIso,
    /// DEFAULT_SHIFT_HOURS), regla general dada explícitamente por el usuario para
    /// empleados sin horario capturado.</summary>
    public static readonly TimeSpan DefaultShiftDuration = TimeSpan.FromHours(8);

    /// <summary>Busca, dentro de TODAS las marcaciones de un solo empleado (cualquier
    /// orden), los turnos que se quedaron abiertos y que ya alcanzaron su hora de corte
    /// según <paramref name="nowUtc"/>. Nunca devuelve más de un turno pendiente por día
    /// calendario.</summary>
    public static IReadOnlyList<PendingAutoClose> FindShiftsToClose(
        IReadOnlyList<Attendance> employeeAttendances, TimeOnly? scheduledEndTime, DateTime nowUtc)
    {
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

            if (DetermineAutoCloseUtc(openEntrada.TimestampUtc, scheduledEndTime, nowUtc) is { } cutoff)
            {
                result.Add(new PendingAutoClose(openEntrada, cutoff));
            }
        }

        return result;
    }

    /// <summary>La hora UTC(-de-pared) exacta en la que debe cerrarse un turno que empezó
    /// en <paramref name="openEntradaUtc"/>, o null si <paramref name="nowUtc"/> todavía no
    /// ha alcanzado ese momento.
    ///
    /// CON <paramref name="scheduledEndTime"/>: el corte es esa hora del mismo día
    /// calendario que la Entrada — salvo turno nocturno (la hora de salida programada "cae
    /// antes" que la de entrada en el reloj de 24h, p. ej. entrada 22:00, salida 06:00),
    /// donde el corte real es al día siguiente.
    ///
    /// SIN <paramref name="scheduledEndTime"/>: el corte es <see cref="DefaultShiftDuration"/>
    /// después de la Entrada — ver comentario de clase.</summary>
    public static DateTime? DetermineAutoCloseUtc(DateTime openEntradaUtc, TimeOnly? scheduledEndTime, DateTime nowUtc)
    {
        DateTime cutoffUtc;
        if (scheduledEndTime is { } end)
        {
            cutoffUtc = DateOnly.FromDateTime(openEntradaUtc).ToDateTime(end, DateTimeKind.Utc);
            if (cutoffUtc <= openEntradaUtc)
            {
                cutoffUtc = cutoffUtc.AddDays(1);
            }
        }
        else
        {
            cutoffUtc = openEntradaUtc + DefaultShiftDuration;
        }

        return nowUtc >= cutoffUtc ? cutoffUtc : null;
    }
}
