using RelojChecador.Application.Payroll;

namespace RelojChecador.Application.Attendances;

/// <summary>
/// Clasifica un día en verde (puntual), amarillo (retardo) o rojo (falta) — pedido
/// explícito del usuario: "verde asistencia puntual, amarillo retardo (tolerancia 10 mns),
/// rojo falta". Lógica pura, sin dependencias de infraestructura, para poder probarla
/// exhaustivamente con xUnit (mismo criterio que
/// RelojChecador.Application.Payroll.WorkedHoursCalculator).
///
/// El color es puramente informativo/visual: nunca cambia cuántas horas se cuentan ni
/// cuánto se paga — eso lo sigue decidiendo únicamente WorkedHoursCalculator.
///
/// Un empleado con <c>HasSpecialSchedule</c> (ver comentario de clase de
/// RelojChecador.Domain.Employees.Employee, pedido explícito del usuario para roles con
/// horario flexible o nocturno como Velador o Gerente) NUNCA sale amarillo ni rojo por
/// esta clasificación — solo verde si se presentó, o neutral en cualquier otro caso. Sigue
/// apareciendo su Falta/Descanso reales en <c>DayAttendanceStatus</c> (eso no se oculta en
/// ningún lado), solo no se le pinta de amarillo/rojo por ello.
/// </summary>
public static class PunctualityClassifier
{
    /// <summary>10 minutos — pedido explícito del usuario ("tolerancia 10 mns").</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(10);

    /// <param name="status">Resultado ya calculado por WorkedHoursCalculator para ese día.</param>
    /// <param name="hasSpecialSchedule">Employee.HasSpecialSchedule — ver comentario de clase.</param>
    /// <param name="scheduledStartTime">Employee.ScheduledStartTime, o null si no está
    /// capturado (en ese caso no hay con qué comparar la hora real de entrada).</param>
    /// <param name="firstPunchUtc">Timestamp de la primera marcación real del día (la que
    /// abrió el turno), o null si no hubo ninguna. Igual que en WorkedHoursCalculator, pese
    /// al nombre es la hora LOCAL del dispositivo, no requiere conversión de huso horario.</param>
    /// <param name="tolerance">Por defecto <see cref="DefaultTolerance"/> — parámetro
    /// explícito (no una constante fija dentro del método) para que quede 100% probable con
    /// xUnit sin depender de un valor implícito.</param>
    public static AttendanceColor Classify(
        DayAttendanceStatus status,
        bool hasSpecialSchedule,
        TimeOnly? scheduledStartTime,
        DateTime? firstPunchUtc,
        TimeSpan? tolerance = null)
    {
        if (hasSpecialSchedule)
        {
            // Horario especial: se ve si trabajó (verde) pero nunca se juzga la hora —
            // Falta/Descanso/Pendiente siguen visibles en DayAttendanceStatus, aquí solo
            // se evita el amarillo/rojo por puntualidad.
            return status == DayAttendanceStatus.Worked ? AttendanceColor.Green : AttendanceColor.Neutral;
        }

        if (status is DayAttendanceStatus.RestDay or DayAttendanceStatus.Pending)
        {
            return AttendanceColor.Neutral;
        }

        if (status == DayAttendanceStatus.Absence)
        {
            return AttendanceColor.Red;
        }

        // Worked, pero sin horario capturado o sin marcación real que comparar: no hay con
        // qué evaluar la puntualidad — nunca se inventa un juicio sin el dato.
        if (scheduledStartTime is null || firstPunchUtc is null)
        {
            return AttendanceColor.Neutral;
        }

        var effectiveTolerance = tolerance ?? DefaultTolerance;
        var actualArrival = TimeOnly.FromDateTime(firstPunchUtc.Value);
        var latestOnTime = scheduledStartTime.Value.Add(effectiveTolerance);

        return actualArrival <= latestOnTime ? AttendanceColor.Green : AttendanceColor.Yellow;
    }
}
