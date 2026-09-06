using RelojChecador.Application.Attendances;
using RelojChecador.Domain.Attendances;

namespace RelojChecador.Application.Tests.Attendances;

public class AttendanceAutoCloserTests
{
    private static DateTime At(int day, int hour, int minute) => new(2026, 8, day, hour, minute, 0, DateTimeKind.Utc);

    private static Attendance Punch(DateTime timestampUtc, int? punchType, Guid? deviceId = null) =>
        Attendance.Create(
            deviceId ?? DeviceId, branchId: Guid.NewGuid(), deviceUserPin: "7",
            timestampUtc: timestampUtc, verifyMethod: AttendanceVerifyMethod.Fingerprint,
            punchType: punchType, rawPayload: "ZK|7|1|0");

    private static readonly Guid DeviceId = Guid.NewGuid();

    [Fact]
    public void FindShiftsToClose_SinHorarioCapturado_NuncaCierraNada()
    {
        var entrada = Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode);

        var result = AttendanceAutoCloser.FindShiftsToClose([entrada], scheduledEndTime: null, nowUtc: At(31, 23, 0));

        Assert.Empty(result);
    }

    [Fact]
    public void FindShiftsToClose_TurnoYaCerradoConSalida_NoHayNadaQueCerrar()
    {
        var attendances = new[]
        {
            Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode),
            Punch(At(31, 16, 0), ShiftPunchTypeClassifier.SalidaCode),
        };

        var result = AttendanceAutoCloser.FindShiftsToClose(attendances, new TimeOnly(16, 0), At(31, 20, 0));

        Assert.Empty(result);
    }

    [Fact]
    public void FindShiftsToClose_TurnoAbiertoAntesDeSuHoraProgramada_TodaviaNoSeCierra()
    {
        var entrada = Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode);

        // Horario de salida 16:00, "ahora" son las 15:59 — todavía no debe cerrarse.
        var result = AttendanceAutoCloser.FindShiftsToClose([entrada], new TimeOnly(16, 0), At(31, 15, 59));

        Assert.Empty(result);
    }

    [Fact]
    public void FindShiftsToClose_TurnoAbiertoTrasSuHoraProgramada_SeCierraExactoAEsaHora()
    {
        var entrada = Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode);

        var result = AttendanceAutoCloser.FindShiftsToClose([entrada], new TimeOnly(16, 0), At(31, 16, 0));

        var pending = Assert.Single(result);
        Assert.Same(entrada, pending.OpenEntrada);
        Assert.Equal(At(31, 16, 0), pending.CutoffUtc);
    }

    [Fact]
    public void FindShiftsToClose_ChecadaDeMasAntesDeLaHoraProgramada_SigueContandoDesdeLaEntradaOriginal()
    {
        // Entrada a las 8:00, otra checada "de más" a las 10:00 (ninguna Salida real) —
        // el turno sigue abierto desde las 8:00, mismo criterio que ShiftPunchTypeClassifier.
        var attendances = new[]
        {
            Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode),
            Punch(At(31, 10, 0), ShiftPunchTypeClassifier.EntradaCode),
        };

        var result = AttendanceAutoCloser.FindShiftsToClose(attendances, new TimeOnly(16, 0), At(31, 16, 0));

        var pending = Assert.Single(result);
        Assert.Equal(At(31, 8, 0), pending.OpenEntrada.TimestampUtc);
    }

    [Fact]
    public void FindShiftsToClose_MarcacionViejaSinClasificar_SeTrataComoEntradaAbierta()
    {
        var entrada = Punch(At(31, 8, 0), punchType: null);

        var result = AttendanceAutoCloser.FindShiftsToClose([entrada], new TimeOnly(16, 0), At(31, 16, 0));

        Assert.Single(result);
    }

    [Fact]
    public void FindShiftsToClose_UnDiaNuncaHeredaElTurnoAbiertoDelDiaAnterior()
    {
        // Turno del día 30 se quedó abierto (nunca cerró) y el día 31 empieza con una
        // Entrada nueva, ya cerrada con su Salida — cada día se evalúa por separado, así
        // que hay UN pendiente (el del día 30), no cero ni dos.
        var attendances = new[]
        {
            Punch(At(30, 8, 0), ShiftPunchTypeClassifier.EntradaCode),
            Punch(At(31, 8, 0), ShiftPunchTypeClassifier.EntradaCode),
            Punch(At(31, 16, 0), ShiftPunchTypeClassifier.SalidaCode),
        };

        var result = AttendanceAutoCloser.FindShiftsToClose(attendances, new TimeOnly(16, 0), At(31, 20, 0));

        var pending = Assert.Single(result);
        Assert.Equal(At(30, 8, 0), pending.OpenEntrada.TimestampUtc);
        Assert.Equal(At(30, 16, 0), pending.CutoffUtc);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Turno nocturno — la hora de salida programada "cae antes" que la de entrada en el
    // reloj de 24h (p. ej. velador: entrada 22:00, salida 06:00).
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DetermineAutoCloseUtc_TurnoNocturno_ElCorteEsAlDiaSiguiente()
    {
        var entradaUtc = At(31, 22, 0);
        var scheduledEndTime = new TimeOnly(6, 0);

        // A las 23:00 del mismo día (31) todavía no debe cerrarse — el corte real es a
        // las 6:00 del día 1, no a las 6:00 (ya pasadas) del propio día 31.
        Assert.Null(AttendanceAutoCloser.DetermineAutoCloseUtc(entradaUtc, scheduledEndTime, At(31, 23, 0)));

        var cutoff = AttendanceAutoCloser.DetermineAutoCloseUtc(entradaUtc, scheduledEndTime, new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), cutoff);
    }
}
