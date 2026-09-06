namespace RelojChecador.Domain.Attendances;

/// <summary>Método con el que un dispositivo verificó al empleado en una marcación —
/// duplica intencionalmente a RelojChecador.Application.Devices.VerifyMethod. El Domain
/// no puede referenciar Application (dirección de dependencia invertida en Clean
/// Architecture), así que quien traduce de un enum al otro es la capa de aplicación al
/// construir un Attendance a partir de un RawAttendanceRecord del adaptador.</summary>
public enum AttendanceVerifyMethod
{
    Unknown = 0,
    Fingerprint = 1,
    Password = 2,
    Card = 3,
    Face = 4,

    /// <summary>Capturada a mano desde la pantalla de Asistencia (ver
    /// AttendanceViewModel.CreateManualAttendanceAsync) — nunca viene de un dispositivo
    /// real. Distinta de las demás a propósito, para que la UI/el Dashboard puedan
    /// distinguir con claridad una corrección manual de una marcación biométrica real.</summary>
    Manual = 5,

    /// <summary>Generada sola por el sistema (ver
    /// RelojChecador.Application.Attendances.AttendanceAutoCloser) cuando un empleado
    /// marcó Entrada pero nunca volvió a checar — pedido explícito del usuario: "si el
    /// empleado no checa a su hora de salida esta se marca automáticamente para que no
    /// sigan corriendo las horas". Nunca viene de un dispositivo real ni de una captura a
    /// mano; distinta de <see cref="Manual"/> a propósito, para que quede clara la
    /// diferencia entre "el administrador la corrigió" y "el sistema la cerró solo".</summary>
    Automatic = 6,
}
