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
}
