using RelojChecador.Application.Payroll;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Payroll;

public class WorkedHoursCalculatorTests
{
    private static readonly Guid DeviceId = Guid.NewGuid();
    private static readonly Guid BranchId = Guid.NewGuid();

    private static Attendance CreateAttendance(DateTime timestampUtc, int? punchType) =>
        Attendance.Create(
            DeviceId, BranchId, "7", timestampUtc, AttendanceVerifyMethod.Fingerprint, punchType, "raw");

    private static Employee CreateSampleEmployee(decimal? weeklySalary = 2500m, decimal? overtimeHourlyRate = null) =>
        Employee.Create(
            EmployeeNumber.Create("0114"), "Ana Torres", Guid.NewGuid(), new DateOnly(2024, 3, 1), weeklySalary,
            overtimeHourlyRate: overtimeHourlyRate);

    // ---- WeekBoundary ----

    [Theory]
    [InlineData(2026, 8, 10, 2026, 8, 10)] // lunes → se queda igual
    [InlineData(2026, 8, 11, 2026, 8, 10)] // martes → lunes de esa semana
    [InlineData(2026, 8, 15, 2026, 8, 10)] // sábado → lunes de esa semana
    [InlineData(2026, 8, 16, 2026, 8, 10)] // domingo → lunes de esa semana (no la siguiente)
    public void GetWeekStart_DevuelveElLunesDeEsaSemana(int y, int m, int d, int expectedY, int expectedM, int expectedD)
    {
        var weekStart = WeekBoundary.GetWeekStart(new DateOnly(y, m, d));

        Assert.Equal(new DateOnly(expectedY, expectedM, expectedD), weekStart);
    }

    // ---- CalculateDay ----

    [Fact]
    public void CalculateDay_TurnoNormalSimple_CalculaHorasSinAdvertencias()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.FromHours(9), summary.RegularTime);
        Assert.Equal(TimeSpan.Zero, summary.OvertimeTime);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConDescanso_DescuentaElTiempoDeDescanso()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc), punchType: 2),
            CreateAttendance(new DateTime(2026, 8, 10, 13, 0, 0, DateTimeKind.Utc), punchType: 3),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.FromHours(8), summary.RegularTime);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConTiempoExtra_LoSumaAparteDelTurnoNormal()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
            CreateAttendance(new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc), punchType: 4),
            CreateAttendance(new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc), punchType: 5),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.FromHours(9), summary.RegularTime);
        Assert.Equal(TimeSpan.FromHours(2), summary.OvertimeTime);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_SinMarcaciones_DevuelveCeroSinAdvertencias()
    {
        var summary = WorkedHoursCalculator.CalculateDay(new DateOnly(2026, 8, 10), []);

        Assert.Equal(TimeSpan.Zero, summary.RegularTime);
        Assert.Equal(TimeSpan.Zero, summary.OvertimeTime);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConEntradaSinSalida_NoSumaYAdvierte()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.Zero, summary.RegularTime);
        Assert.Single(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConSalidaSinEntrada_NoSumaYAdvierte()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.Zero, summary.RegularTime);
        Assert.Single(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConDosEntradasSeguidas_UsaLaMasRecienteYAdvierte()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        // Se usa la entrada de las 9:00 (la más reciente antes de la salida) — 8 horas, no 9.
        Assert.Equal(TimeSpan.FromHours(8), summary.RegularTime);
        Assert.Single(summary.Warnings);
    }

    [Fact]
    public void CalculateDay_ConMarcacionSinPunchType_SeIgnoraParaElCalculo()
    {
        var date = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc), punchType: null),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
        };

        var summary = WorkedHoursCalculator.CalculateDay(date, attendances);

        Assert.Equal(TimeSpan.FromHours(9), summary.RegularTime);
        Assert.Empty(summary.Warnings);
    }

    // ---- CalculateWeek ----

    [Fact]
    public void CalculateWeek_SumaVariosDiasYCalculaPagoDeHorasExtra()
    {
        var employee = CreateSampleEmployee(weeklySalary: 3000m, overtimeHourlyRate: 100m);
        var weekStart = new DateOnly(2026, 8, 10); // lunes
        var attendances = new[]
        {
            // Lunes: 9h normales
            CreateAttendance(new DateTime(2026, 8, 10, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
            // Martes: 9h normales + 2h extra
            CreateAttendance(new DateTime(2026, 8, 11, 8, 0, 0, DateTimeKind.Utc), punchType: 0),
            CreateAttendance(new DateTime(2026, 8, 11, 17, 0, 0, DateTimeKind.Utc), punchType: 1),
            CreateAttendance(new DateTime(2026, 8, 11, 18, 0, 0, DateTimeKind.Utc), punchType: 4),
            CreateAttendance(new DateTime(2026, 8, 11, 20, 0, 0, DateTimeKind.Utc), punchType: 5),
        };

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, attendances);

        Assert.Equal(TimeSpan.FromHours(18), summary.TotalRegularTime);
        Assert.Equal(TimeSpan.FromHours(2), summary.TotalOvertimeTime);
        Assert.Equal(200m, summary.OvertimePay); // 2h × $100
        Assert.Equal(3200m, summary.TotalPay); // $3000 sueldo + $200 horas extra
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateWeek_ConHorasExtraSinTarifaCapturada_AdviertaYNoCalculaPago()
    {
        var employee = CreateSampleEmployee(weeklySalary: 3000m, overtimeHourlyRate: null);
        var weekStart = new DateOnly(2026, 8, 10);
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 18, 0, 0, DateTimeKind.Utc), punchType: 4),
            CreateAttendance(new DateTime(2026, 8, 10, 20, 0, 0, DateTimeKind.Utc), punchType: 5),
        };

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, attendances);

        Assert.Equal(TimeSpan.FromHours(2), summary.TotalOvertimeTime);
        Assert.Equal(0m, summary.OvertimePay);
        Assert.Equal(3000m, summary.TotalPay); // solo el sueldo semanal, sin pago de horas extra
        Assert.Single(summary.Warnings);
    }

    [Fact]
    public void CalculateWeek_SinMarcaciones_SoloElSueldoSemanal()
    {
        var employee = CreateSampleEmployee(weeklySalary: 2500m);
        var weekStart = new DateOnly(2026, 8, 10);

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, []);

        Assert.Equal(TimeSpan.Zero, summary.TotalRegularTime);
        Assert.Equal(TimeSpan.Zero, summary.TotalOvertimeTime);
        Assert.Equal(2500m, summary.TotalPay);
        Assert.Empty(summary.Warnings);
    }

    [Fact]
    public void CalculateWeek_DevuelveElRangoCompletoDeLunesADomingo()
    {
        var employee = CreateSampleEmployee();
        var weekStart = new DateOnly(2026, 8, 10); // lunes

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, []);

        Assert.Equal(new DateOnly(2026, 8, 10), summary.WeekStart);
        Assert.Equal(new DateOnly(2026, 8, 16), summary.WeekEnd); // domingo
    }

    // --- Sueldo pendiente de captura (WeeklySalary: null) — caso real que motivó volver
    // el campo nullable: nunca se debe tratar como $0 en el cálculo de nómina. ---

    [Fact]
    public void CalculateWeek_ConSueldoSemanalNulo_AdviertaYNoLoSumaComoCero()
    {
        var employee = CreateSampleEmployee(weeklySalary: null);
        var weekStart = new DateOnly(2026, 8, 10);

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, []);

        Assert.Null(summary.WeeklySalary);
        Assert.Equal(0m, summary.TotalPay);
        Assert.Contains(summary.Warnings, w => w.Contains("pendiente de captura"));
    }

    [Fact]
    public void CalculateWeek_ConSueldoNuloYHorasExtraConTarifa_TotalPaySoloIncluyeLasHorasExtra()
    {
        var employee = CreateSampleEmployee(weeklySalary: null, overtimeHourlyRate: 100m);
        var weekStart = new DateOnly(2026, 8, 10); // lunes
        var attendances = new[]
        {
            CreateAttendance(new DateTime(2026, 8, 10, 17, 0, 0, DateTimeKind.Utc), punchType: 4),
            CreateAttendance(new DateTime(2026, 8, 10, 19, 0, 0, DateTimeKind.Utc), punchType: 5),
        };

        var summary = WorkedHoursCalculator.CalculateWeek(employee, weekStart, attendances);

        Assert.Equal(200m, summary.OvertimePay); // 2h * 100
        Assert.Equal(200m, summary.TotalPay); // sin sueldo base, solo horas extra
    }
}
