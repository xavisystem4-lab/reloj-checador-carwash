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
    public void Create_ConVerifyMethodManualYEmployeeId_QuedaVinculadaDesdeElInicio()
    {
        // Caso real: "Marcar asistencia manual" (AttendanceViewModel.CreateManualAttendanceAsync)
        // siempre conoce el empleado de antemano (lo elige quien captura) — a diferencia
        // de una marcación real del dispositivo, no necesita conciliación posterior.
        var employeeId = Guid.NewGuid();
        var attendance = Attendance.Create(
            Guid.NewGuid(), Guid.NewGuid(), "MANUAL", DateTime.UtcNow,
            AttendanceVerifyMethod.Manual, punchType: 0, rawPayload: "MANUAL|Juan Pérez", employeeId);

        Assert.Equal(AttendanceVerifyMethod.Manual, attendance.VerifyMethod);
        Assert.Equal(employeeId, attendance.EmployeeId);
    }

    [Fact]
    public void Create_ConPinVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Attendance.Create(
                Guid.NewGuid(), Guid.NewGuid(), "  ", DateTime.UtcNow, AttendanceVerifyMethod.Unknown, null, "raw"));
    }

    [Fact]
    public void ReconcileEmployee_VinculaElEmpleadoYSuSucursalSinTocarLaMarcacionOriginal()
    {
        var attendance = CreateSample();
        var employeeId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var originalPayload = attendance.RawPayload;
        var originalTimestamp = attendance.TimestampUtc;

        attendance.ReconcileEmployee(employeeId, branchId);

        Assert.Equal(employeeId, attendance.EmployeeId);
        // La sucursal SIEMPRE se deriva del empleado, nunca del dispositivo — un solo reloj
        // físico puede recibir marcaciones de empleados de varias sucursales distintas.
        Assert.Equal(branchId, attendance.BranchId);
        // La marcación tal cual la reportó el dispositivo es inmutable — solo cambia el vínculo.
        Assert.Equal(originalPayload, attendance.RawPayload);
        Assert.Equal(originalTimestamp, attendance.TimestampUtc);
    }

    [Fact]
    public void Create_SinSucursalConocida_QuedaPendienteDeAsignacion()
    {
        // Caso real: un PIN del reloj que todavía no está vinculado a ningún Employee — la
        // marcación se guarda igual (nunca se pierde), sin sucursal ni empleado, hasta que
        // se cree el vínculo (ver DevicesViewModel.PersistAttendanceAsync/
        // EmployeesViewModel.ReconcileAttendancesAsync).
        var attendance = Attendance.Create(
            Guid.NewGuid(), branchId: null, deviceUserPin: "9", DateTime.UtcNow,
            AttendanceVerifyMethod.Fingerprint, punchType: 0, rawPayload: "ZK|9|1|0");

        Assert.Null(attendance.BranchId);
        Assert.Null(attendance.EmployeeId);
    }

    [Fact]
    public void ReconcileEmployee_ConNull_DejaLaMarcacionPendienteDeNuevo()
    {
        var attendance = CreateSample();
        attendance.ReconcileEmployee(Guid.NewGuid(), Guid.NewGuid());

        attendance.ReconcileEmployee(null, null);

        Assert.Null(attendance.EmployeeId);
        Assert.Null(attendance.BranchId);
    }
}
