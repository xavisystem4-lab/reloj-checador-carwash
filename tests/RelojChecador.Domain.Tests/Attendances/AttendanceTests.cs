using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Tests.Attendances;

public class AttendanceTests
{
    private static Attendance CreateSample(Guid? deviceId = null, Guid? branchId = null) =>
        Attendance.Create(
            deviceId ?? Guid.NewGuid(),
            branchId ?? Guid.NewGuid(),
            deviceUserPin: "7",
            timestampUtc: new DateTime(2026, 8, 13, 8, 2, 0, DateTimeKind.Utc),
            verifyMethod: AttendanceVerifyMethod.Fingerprint,
            punchType: 0,
            rawPayload: "ZK|7|1|0|2026-08-13T08:02:00Z");

    [Fact]
    public void Create_ConValoresValidos_QuedaSinEmpleadoVinculado()
    {
        var attendance = CreateSample();

        // Nunca se asume una conciliación con Employee que no se pidió explícitamente.
        Assert.Null(attendance.EmployeeId);
        Assert.Equal("7", attendance.DeviceUserPin);
        Assert.Equal(AttendanceVerifyMethod.Fingerprint, attendance.VerifyMethod);
    }

    [Fact]
    public void Create_NormalizaLaMarcaDeTiempoAUtc()
    {
        var unspecified = new DateTime(2026, 8, 13, 8, 2, 0, DateTimeKind.Unspecified);

        var attendance = Attendance.Create(
            Guid.NewGuid(), Guid.NewGuid(), "7", unspecified, AttendanceVerifyMethod.Fingerprint, 0, "raw");

        Assert.Equal(DateTimeKind.Utc, attendance.TimestampUtc.Kind);
    }

    [Fact]
    public void Create_ConPinVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Attendance.Create(
                Guid.NewGuid(), Guid.NewGuid(), "  ", DateTime.UtcNow, AttendanceVerifyMethod.Unknown, null, "raw"));
    }

    [Fact]
    public void ReconcileEmployee_VinculaElEmpleadoSinTocarLaMarcacionOriginal()
    {
        var attendance = CreateSample();
        var employeeId = Guid.NewGuid();
        var originalPayload = attendance.RawPayload;
        var originalTimestamp = attendance.TimestampUtc;

        attendance.ReconcileEmployee(employeeId);

        Assert.Equal(employeeId, attendance.EmployeeId);
        // La marcación tal cual la reportó el dispositivo es inmutable — solo cambia el vínculo.
        Assert.Equal(originalPayload, attendance.RawPayload);
        Assert.Equal(originalTimestamp, attendance.TimestampUtc);
    }
}
