namespace RelojChecador.Application.Attendances;

/// <summary>Semáforo de puntualidad de un día — ver <see cref="PunctualityClassifier"/>.
/// Puramente visual/de reporte, nunca afecta el cálculo de horas ni de sueldo (ver
/// RelojChecador.Application.Payroll.WorkedHoursCalculator).</summary>
public enum AttendanceColor
{
    /// <summary>Sin juicio de puntualidad: día de descanso, día pendiente (semana en
    /// curso) o empleado con horario especial que no aplica la regla.</summary>
    Neutral = 0,

    /// <summary>Trabajó y llegó dentro de la tolerancia — o tiene horario especial y sí
    /// se presentó (no se juzga la hora exacta).</summary>
    Green = 1,

    /// <summary>Trabajó pero llegó después de la tolerancia respecto a su hora de
    /// entrada esperada.</summary>
    Yellow = 2,

    /// <summary>Falta: sin marcación y ya se tomó el día de descanso de la semana.</summary>
    Red = 3,
}
