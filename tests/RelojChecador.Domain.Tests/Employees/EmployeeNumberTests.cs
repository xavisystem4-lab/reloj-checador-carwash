using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Domain.Tests.Employees;

public class EmployeeNumberTests
{
    [Fact]
    public void Create_RecortaEspacios()
    {
        var number = EmployeeNumber.Create("  0114  ");
        Assert.Equal("0114", number.Value);
    }

    [Fact]
    public void Create_ConValorVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() => EmployeeNumber.Create(" "));
    }

    [Fact]
    public void Create_ConMasDe20Caracteres_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() => EmployeeNumber.Create(new string('9', 21)));
    }

    [Fact]
    public void Igualdad_SeBasaEnElValor_NoEnIdentidad()
    {
        var a = EmployeeNumber.Create("0114");
        var b = EmployeeNumber.Create("0114");

        Assert.Equal(a, b);
        Assert.True(a == b);
    }
}
