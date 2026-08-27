using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Attendances;

/// <summary>
/// Una marcación de asistencia ya persistida localmente — tal cual llegó de un
/// dispositivo (por descarga manual o por el monitoreo en tiempo real, ver
/// IAttendanceDeviceAdapter.AttendancePunchReceived), o capturada a mano desde la pantalla
/// de Asistencia cuando a alguien se le olvidó checar (ver
/// AttendanceViewModel.CreateManualAttendanceAsync — <see cref="AttendanceVerifyMethod.Manual"/>
/// la distingue de una marcación biométrica real). Nunca se EDITA ni se BORRA desde la UI
/// en ninguno de los dos casos: es un registro de auditoría de negocio (nómina,
/// incidencias), no un dato de trabajo — la única vía de escritura es <see cref="Create"/>,
/// una corrección posterior siempre es una fila NUEVA, nunca un cambio sobre una ya
/// existente.
///
/// <see cref="EmployeeId"/> es nullable a propósito: puede llegar una marcación de un PIN
/// de dispositivo que todavía no está vinculado a ningún Employee
/// (ver EmployeeDeviceMappings) — se guarda igual, sin perderla, para poder conciliarla
/// después en vez de descartarla silenciosamente.
///
/// <see cref="BranchId"/> también es nullable — pedido explícito del usuario ("separar
/// claramente el concepto de reloj/dispositivo del concepto de sucursal"): un solo reloj
/// físico puede recibir marcaciones de empleados de VARIAS sucursales distintas, así que la
/// sucursal de una marcación se resuelve siempre por el EMPLEADO (Employee.BranchId), NUNCA
/// por el dispositivo que la reportó — <see cref="DeviceId"/> ya no implica ninguna
/// sucursal. Si el PIN todavía no está vinculado a ningún Employee, tampoco se conoce su
/// sucursal — <see cref="BranchId"/> queda en null exactamente en los mismos casos que
/// <see cref="EmployeeId"/> queda en null (misma causa, mismo momento: ver
/// DevicesViewModel.PersistAttendanceAsync), y ambos se resuelven juntos al conciliarse
/// (ver <see cref="ReconcileEmployee"/>) — nunca se pierde la marcación, solo queda
/// "pendiente de asignación" hasta que exista el vínculo.
/// </summary>
public sealed class Attendance : AuditableEntity
{
    public Guid DeviceId { get; private set; }
    public Guid? BranchId { get; private set; }
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
        Guid? branchId,
        string deviceUserPin,
        DateTime timestampUtc,
        AttendanceVerifyMethod verifyMethod,
        int? punchType,
        string rawPayload,
        Guid? employeeId = null)
    {
        Guard.AgainstEmptyGuid(deviceId, nameof(deviceId));
        if (branchId is { } branchIdValue)
        {
            Guard.AgainstEmptyGuid(branchIdValue, nameof(branchId));
        }
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
    /// <paramref name="branchId"/> se actualiza EN CONJUNTO con <paramref name="employeeId"/>
    /// a propósito: la sucursal de una marcación siempre se deriva del empleado (ver
    /// comentario de clase), nunca es un dato independiente que alguien pueda fijar solo —
    /// quien llama a esto ya conoce Employee.BranchId en el momento de conciliar (ver
    /// EmployeesViewModel.ReconcileAttendancesAsync). No cambia ningún otro campo: la
    /// marcación en sí, tal como la reportó el dispositivo, es inmutable.</summary>
    public void ReconcileEmployee(Guid? employeeId, Guid? branchId)
    {
        EmployeeId = employeeId;
        BranchId = branchId;
        Touch();
    }
}
