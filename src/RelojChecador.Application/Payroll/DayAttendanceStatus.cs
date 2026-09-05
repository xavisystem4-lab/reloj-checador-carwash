namespace RelojChecador.Application.Payroll;

/// <summary>Clasificación de un día calendario dentro de la semana de nómina — ver regla
/// completa en el comentario de clase de <see cref="WorkedHoursCalculator"/>.</summary>
public enum DayAttendanceStatus
{
    /// <summary>Hubo al menos una marcación ese día (independientemente de si el cálculo
    /// de horas dio advertencias — p. ej. una entrada sin su salida sigue siendo un día
    /// "trabajado": el empleado sí se presentó).</summary>
    Worked = 0,

    /// <summary>No hay ninguna marcación ese día y es el primer día así de la semana
    /// (en orden lunes→domingo) — se asume el día de descanso semanal del empleado.</summary>
    RestDay = 1,

    /// <summary>No hay ninguna marcación ese día y la semana ya tomó su día de descanso
    /// en un día anterior — un empleado solo descansa 1 día por semana.</summary>
    Absence = 2,

    /// <summary>El día todavía no ocurre (es hoy o es futuro dentro de la semana en
    /// curso) — no se puede saber todavía si el empleado va a checar, así que no se marca
    /// ni como descanso ni como falta.</summary>
    Pending = 3,
}
