using RelojChecador.Application.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeNumberPinRulesTests
{
    [Theory]
    [InlineData("EMP-012", "12")]
    [InlineData("EMP-001", "1")]
    [InlineData("emp-100", "100")]
    [InlineData("7", "7")]
    [InlineData(" 055 ", "55")]
    public void ToDevicePin_TomaLosDigitosFinalesSinCeros(string number, string expected) =>
        Assert.Equal(expected, EmployeeNumberPinRules.ToDevicePin(number));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("EMP-000")]
    [InlineData("12-A")]
    public void ToDevicePin_SinDigitosValidos_DevuelveNull(string? number) =>
        Assert.Null(EmployeeNumberPinRules.ToDevicePin(number));

    [Fact]
    public void NextNumber_SigueAlMayorEmpIgnorandoNumerosViejos() =>
        Assert.Equal("EMP-055", EmployeeNumberPinRules.NextNumber(["EMP-001", "EMP-054", "12", "201", "EMP-009"]));

    [Fact]
    public void NextNumber_SinCatalogoEmp_EmpiezaEnUno() =>
        Assert.Equal("EMP-001", EmployeeNumberPinRules.NextNumber(["12", "39"]));

    [Fact]
    public void NextNumber_RespetaAnchoMayorATres() =>
        Assert.Equal("EMP-1000", EmployeeNumberPinRules.NextNumber(["EMP-999"]));
}
