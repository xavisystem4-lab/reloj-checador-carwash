using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Domain.Tests.Employees;

public class EmployeeTests
{
    private const decimal SampleWeeklySalary = 2500m;

    private static Employee CreateSampleEmployee(Guid? branchId = null) =>
        Employee.Create(
            EmployeeNumber.Create("0114"),
            "Ana Torres",
            branchId ?? Guid.NewGuid(),
            new DateOnly(2024, 3, 1),
            SampleWeeklySalary,
            department: "Operaciones",
            position: "Cajera");

    [Fact]
    public void Create_ConValoresValidos_QuedaActivoPorDefecto()
    {
        var employee = CreateSampleEmployee();

        Assert.Equal(EmploymentStatus.Active, employee.Status);
        Assert.Equal("0114", employee.Number.Value);
        Assert.Equal("Ana Torres", employee.FullName);
        Assert.Equal(SampleWeeklySalary, employee.WeeklySalary);
        Assert.Null(employee.OvertimeHourlyRate);
    }

    [Fact]
    public void Create_ConSucursalVacia_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Employee.Create(
                EmployeeNumber.Create("0114"), "Ana Torres", Guid.Empty, new DateOnly(2024, 3, 1), SampleWeeklySalary));
    }

    [Fact]
    public void Create_ConSueldoSemanalNegativo_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Employee.Create(
                EmployeeNumber.Create("0114"), "Ana Torres", Guid.NewGuid(), new DateOnly(2024, 3, 1), -1m));
    }

    [Fact]
    public void Create_ConTarifaDeHoraExtraNegativa_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Employee.Create(
                EmployeeNumber.Create("0114"), "Ana Torres", Guid.NewGuid(), new DateOnly(2024, 3, 1), SampleWeeklySalary,
                overtimeHourlyRate: -1m));
    }

    [Fact]
    public void TransferToBranch_CambiaLaSucursalAsignada()
    {
        var employee = CreateSampleEmployee();
        var newBranchId = Guid.NewGuid();

        employee.TransferToBranch(newBranchId);

        Assert.Equal(newBranchId, employee.BranchId);
    }

    [Fact]
    public void UpdateFiscalInfo_NormalizaRfcYCurpAMayusculas()
    {
        var employee = CreateSampleEmployee();

        employee.UpdateFiscalInfo("toma850101abc", "toma850101hbcnrn01", "12345678901");

        Assert.Equal("TOMA850101ABC", employee.Rfc);
        Assert.Equal("TOMA850101HBCNRN01", employee.Curp);
        Assert.Equal("12345678901", employee.Nss);
    }

    [Fact]
    public void ChangeStatus_ActualizaEstadoYAuditoria()
    {
        var employee = CreateSampleEmployee();

        employee.ChangeStatus(EmploymentStatus.Terminated);

        Assert.Equal(EmploymentStatus.Terminated, employee.Status);
    }

    [Fact]
    public void ChangeNumber_ActualizaElNumeroDeEmpleado()
    {
        var employee = CreateSampleEmployee();

        employee.ChangeNumber(EmployeeNumber.Create("0250"));

        Assert.Equal("0250", employee.Number.Value);
    }

    [Fact]
    public void ChangeNumber_ConNumeroNulo_LanzaArgumentNullException()
    {
        var employee = CreateSampleEmployee();

        Assert.Throws<ArgumentNullException>(() => employee.ChangeNumber(null!));
    }

    [Fact]
    public void UpdateCompensation_ActualizaSueldoYTarifaDeHoraExtra()
    {
        var employee = CreateSampleEmployee();

        employee.UpdateCompensation(3000m, 85m);

        Assert.Equal(3000m, employee.WeeklySalary);
        Assert.Equal(85m, employee.OvertimeHourlyRate);
    }

    [Fact]
    public void UpdateCompensation_ConTarifaNula_QuitaLaTarifaDeHoraExtra()
    {
        var employee = CreateSampleEmployee();
        employee.UpdateCompensation(3000m, 85m);

        employee.UpdateCompensation(3000m, null);

        Assert.Null(employee.OvertimeHourlyRate);
    }

    [Fact]
    public void UpdateCompensation_ConSueldoNegativo_LanzaDomainException()
    {
        var employee = CreateSampleEmployee();

        Assert.Throws<DomainException>(() => employee.UpdateCompensation(-1m, null));
    }
}
