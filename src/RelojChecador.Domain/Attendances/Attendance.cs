using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Attendances;

/// <summary>
/// Una marcación de asistencia ya persistida localmente, tal cual llegó de un
/// dispositivo (por descarga manual o por el monitoreo en tiempo real —
/// ver IAttendanceDeviceAdapter.AttendancePunchReceived). Nunca se edita ni se borra
/// desde la UI: es un registro de auditoría de negocio (nómina, incidencias), no un dato
/// de trabajo — la única vía de escritura es <see cref="Create"/>.
///
/// <see cref="EmployeeId"/> es nullable a propósito: puede llegar una marcación de un PIN
/// de dispositivo que todavía no está vinculado a ningún Employee
/// (ver EmployeeDeviceMappings) — se guarda igual, sin perderla, para poder conciliarla
/// después en vez de descartarla silenciosamente.
/// </summary>
public sealed class Attendance : AuditableEntity
{
    public Guid DeviceId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid? EmployeeId { get; private set; }
    public string DeviceUserPin { get; private set; } = null!;
    public DateTime TimestampUtc { get; private set; }
    public AttendanceVerifyMethod VerifyMethod { get; private set; }
    public int? PunchType { get; private set; }

    /// <summary>Representación original tal cual la entregó el dispositivo — se conserva
    /// siempre, incluso si el registro se reprocesa o se concilia con un Employee más
    /// adelante, para auditoría.</summary>
    public string RawPayload { get; private set; } = null!;

    private Attendance()
    {
        // Constructor privado para EF Core.
    }

    public static Attendance Create(
        Guid deviceId,
        Guid branchId,
        string deviceUserPin,
        DateTime timestampUtc,
        AttendanceVerifyMethod verifyMethod,
        int? punchType,
        string rawPayload,
        Guid? employeeId = null)
    {
        Guard.AgainstEmptyGuid(deviceId, nameof(deviceId));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        Guard.AgainstNullOrWhiteSpace(deviceUserPin, nameof(deviceUserPin));
        Guard.AgainstNullOrWhiteSpace(rawPayload, nameof(rawPayload));

        var attendance = new Attendance
        {
            Id = Guid.CreateVersion7(),
            DeviceId = deviceId,
            BranchId = branchId,
            EmployeeId = employeeId,
            DeviceUserPin = deviceUserPin.Trim(),
            TimestampUtc = DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc),
            VerifyMethod = verifyMethod,
            PunchType = punchType,
            RawPayload = rawPayload,
        };
        attendance.InitializeAuditFields();
        return attendance;
    }

    /// <summary>Vincula (o desvincula, con null) esta marcación a un Employee ya
    /// identificado — p. ej. al crear tardíamente el EmployeeDeviceMapping que faltaba.
    /// No cambia ningún otro campo: la marcación en sí, tal como la reportó el
    /// dispositivo, es inmutable.</summary>
    public void ReconcileEmployee(Guid? employeeId)
    {
        EmployeeId = employeeId;
        Touch();
    }
}
