using RelojChecador.Application.Attendances;
using RelojChecador.Application.Payroll;

namespace RelojChecador.Application.Tests.Attendances;

public class PunctualityClassifierTests
{
    private static DateTime PunchAt(int hour, int minute) =>
        new(2026, 8, 10, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Classify_LlegaExactoAHoraProgramada_EsVerde()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: false, new TimeOnly(8, 0), PunchAt(8, 0));

        Assert.Equal(AttendanceColor.Green, color);
    }

    [Fact]
    public void Classify_LlegaDentroDeLaTolerancia_EsVerde()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: false, new TimeOnly(8, 0), PunchAt(8, 10));

        Assert.Equal(AttendanceColor.Green, color);
    }

    [Fact]
    public void Classify_LlegaUnMinutoDespuesDeLaTolerancia_EsAmarillo()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: false, new TimeOnly(8, 0), PunchAt(8, 11));

        Assert.Equal(AttendanceColor.Yellow, color);
    }

    [Fact]
    public void Classify_ToleranciaPersonalizada_SeRespeta()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: false, new TimeOnly(8, 0), PunchAt(8, 20),
            tolerance: TimeSpan.FromMinutes(30));

        Assert.Equal(AttendanceColor.Green, color);
    }

    [Fact]
    public void Classify_Falta_EsRojo()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Absence, hasSpecialSchedule: false, new TimeOnly(8, 0), null);

        Assert.Equal(AttendanceColor.Red, color);
    }

    [Theory]
    [InlineData(nameof(DayAttendanceStatus.RestDay))]
    [InlineData(nameof(DayAttendanceStatus.Pending))]
    public void Classify_DescansoOPendiente_EsNeutral(string statusName)
    {
        var status = Enum.Parse<DayAttendanceStatus>(statusName);

        var color = PunctualityClassifier.Classify(status, hasSpecialSchedule: false, new TimeOnly(8, 0), null);

        Assert.Equal(AttendanceColor.Neutral, color);
    }

    [Fact]
    public void Classify_TrabajadoSinHorarioCapturado_EsNeutral()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: false, scheduledStartTime: null, PunchAt(10, 0));

        Assert.Equal(AttendanceColor.Neutral, color);
    }

    [Fact]
    public void Classify_HorarioEspecialYTrabajo_EsVerdeSinImportarLaHora()
    {
        // Llega muy tarde (10am vs 8am programada) pero tiene horario especial — nunca
        // se juzga la hora exacta (pedido explícito: "excluirlos de retardo/falta automáticos").
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Worked, hasSpecialSchedule: true, new TimeOnly(8, 0), PunchAt(10, 0));

        Assert.Equal(AttendanceColor.Green, color);
    }

    [Fact]
    public void Classify_HorarioEspecialYFalta_EsNeutralNoRojo()
    {
        var color = PunctualityClassifier.Classify(
            DayAttendanceStatus.Absence, hasSpecialSchedule: true, new TimeOnly(8, 0), null);

        Assert.Equal(AttendanceColor.Neutral, color);
    }
}
