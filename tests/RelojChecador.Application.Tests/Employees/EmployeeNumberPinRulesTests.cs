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
}
