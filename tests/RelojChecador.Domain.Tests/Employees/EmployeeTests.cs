using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Domain.Tests.Employees;

public class EmployeeTests
{
    private static Employee CreateSampleEmployee(Guid? branchId = null) =>
        Employee.Create(
            EmployeeNumber.Create("0114"),
            "Ana Torres",
            branchId ?? Guid.NewGuid(),
            new DateOnly(2024, 3, 1),
            department: "Operaciones",
            position: "Cajera");

    [Fact]
    public void Create_ConValoresValidos_QuedaActivoPorDefecto()
    {
        var employee = CreateSampleEmployee();

        Assert.Equal(EmploymentStatus.Active, employee.Status);
        Assert.Equal("0114", employee.Number.Value);
        Assert.Equal("Ana Torres", employee.FullName);
    }

    [Fact]
    public void Create_ConSucursalVacia_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            Employee.Create(EmployeeNumber.Create("0114"), "Ana Torres", Guid.Empty, new DateOnly(2024, 3, 1)));
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
}
