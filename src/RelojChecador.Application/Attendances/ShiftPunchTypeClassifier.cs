namespace RelojChecador.Application.Attendances;

/// <summary>
/// Decide el PunchType (0=Entrada, 1=Salida — misma convención que
/// RelojChecador.WPF.Converters.PunchTypeToTextConverter) de una marcación nueva, en vez de
/// confiar en el <c>dwInOutMode</c> crudo que entrega el reloj (el F22/ID no tiene botones
/// de Entrada/Salida — ese valor no es confiable, ver comentario de PunchTypeToTextConverter).
/// Pedido explícito del usuario: "después de las 7 horas con 50 minutos su segunda checada
/// se marque como salida de turno y si no checan y lo hacen hasta el día siguiente se marque
/// entrada normal".
///
/// Regla: la PRIMERA marcación del día de un empleado es Entrada y "abre" su turno. La
/// siguiente marcación del MISMO día se marca Salida si pasaron >= 7h50 desde esa primera
/// entrada (cierra el turno) O si ya alcanzó su hora de salida programada
/// (<see cref="RelojChecador.Domain.Employees.Employee.ScheduledEndTime"/>, cuando está
/// capturada) — pedido explícito del usuario: "cuando cumpla el ciclo o su horario que diga
/// salida". La segunda condición existe para el turno CORTO (p. ej. 8:00-13:00, 5 horas):
/// sin ella, alguien cuyo horario completo dura menos de 7h50 nunca cerraría su turno solo
/// con la regla de las 7h50. Si ninguna de las dos se cumple, se marca Entrada otra vez pero
/// el turno SIGUE abierto desde la hora original (no se reinicia el conteo). Una marcación en
/// un día DISTINTO nunca hereda un turno abierto del día anterior — siempre es una entrada
/// nueva, aunque nunca haya llegado la salida del día previo.
///
/// <c>TimestampUtc</c> pese al nombre es, en la práctica, la hora LOCAL del
/// dispositivo (ver comentario de ZKTecoDeviceAdapter) — por eso comparar directamente su
/// componente de hora contra <c>scheduledEndTime</c> es válido, sin ninguna conversión de
/// huso horario de por medio.
/// </summary>
public static class ShiftPunchTypeClassifier
{
    public const int EntradaCode = 0;
    public const int SalidaCode = 1;

    private static readonly TimeSpan ShiftDuration = new(7, 50, 0);

    /// <param name="todaysPunchesBeforeNew">Marcaciones YA registradas del MISMO empleado en
    /// el MISMO día calendario que <paramref name="newPunchUtc"/>, con timestamp anterior a
    /// ella, en orden ascendente. Un PunchType null (marcación vieja sin clasificar) se trata
    /// como Entrada — mismo criterio conservador que el resto de la clasificación.</param>
    /// <param name="scheduledEndTime">Hora de salida programada del empleado (Employee.
    /// ScheduledEndTime), o null si no la tiene capturada — en ese caso solo aplica la regla
    /// de las 7h50, igual que antes de que existiera Horario de empleados.</param>
    public static int Classify(
        IReadOnlyList<(DateTime TimestampUtc, int? PunchType)> todaysPunchesBeforeNew,
        DateTime newPunchUtc,
        TimeOnly? scheduledEndTime = null)
    {
        DateTime? openEntradaUtc = null;
        foreach (var (timestamp, punchType) in todaysPunchesBeforeNew)
        {
            if (punchType == SalidaCode)
            {
                openEntradaUtc = null;
            }
            else if (openEntradaUtc is null)
            {
                openEntradaUtc = timestamp;
            }
            // Si ya había un turno abierto y esta fila es OTRA entrada (p. ej. una checada
            // de más antes de cumplir las 7h50 o el horario), el turno sigue abierto desde
            // la hora ORIGINAL — no se reinicia con cada checada de más.
        }

        if (openEntradaUtc is null)
        {
            return EntradaCode;
        }

        var completedCycle = newPunchUtc - openEntradaUtc.Value >= ShiftDuration;
        var reachedScheduledEnd = scheduledEndTime is { } end && TimeOnly.FromDateTime(newPunchUtc) >= end;
        return completedCycle || reachedScheduledEnd ? SalidaCode : EntradaCode;
    }
}
