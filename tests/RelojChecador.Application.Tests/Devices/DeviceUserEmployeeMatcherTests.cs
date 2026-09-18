using RelojChecador.Application.Devices;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Devices;

public class DeviceUserEmployeeMatcherTests
{
    private static readonly IReadOnlySet<string> NoPins = new HashSet<string>();
    private static readonly IReadOnlySet<Guid> NoEmployees = new HashSet<Guid>();

    private static Employee Emp(string number, string name, EmploymentStatus? status = null)
    {
        var employee = Employee.Create(
            EmployeeNumber.Create(number), name, Guid.NewGuid(), new DateOnly(2025, 1, 1), 2500m);
        if (status is { } s && employee.Status != s)
        {
            employee.ChangeStatus(s);
        }
        return employee;
    }

    private static DeviceUserRecord User(string pin, string name) => new(pin, name, 0, true);

    private static DeviceUserMatchResult Single(
        DeviceUserRecord user, IReadOnlyList<Employee> employees,
        IReadOnlySet<string>? linkedPins = null, IReadOnlySet<Guid>? linkedEmployees = null) =>
        Assert.Single(DeviceUserEmployeeMatcher.Match([user], employees, linkedPins ?? NoPins, linkedEmployees ?? NoEmployees));

    [Fact]
    public void NombreIgual_SinImportarMayusculasAcentosNiEspacios_Vincula()
    {
        var jose = Emp("12", "José  Pérez López");

        var result = Single(User("7", "JOSE PEREZ LOPEZ"), [jose, Emp("13", "Ana Torres")]);

        Assert.True(result.ShouldLink);
        Assert.Equal(jose.Id, result.Employee!.Id);
        Assert.Equal(DeviceUserMatchKind.ExactName, result.Kind);
    }

    [Fact]
    public void NumeroDeEmpleadoIgualAlPin_GanaAunqueElNombreDelRelojNoSeParezca()
    {
        var emp = Emp("14", "Adrian Uribe Salazar");
        var otro = Emp("15", "Adri");

        // El reloj tiene un apodo; el número (= PIN) es el criterio confiable.
        var result = Single(User("14", "Adri U."), [emp, otro]);

        Assert.True(result.ShouldLink);
        Assert.Equal(emp.Id, result.Employee!.Id);
        Assert.Equal(DeviceUserMatchKind.EmployeeNumber, result.Kind);
    }

    [Fact]
    public void NumeroDeEmpleado_GanaSobreUnNombreQueApuntaAOtroEmpleado()
    {
        var porNumero = Emp("7", "Jose Perez");
        var porNombre = Emp("8", "Ana Torres");

        var result = Single(User("7", "Ana Torres"), [porNumero, porNombre]);

        Assert.Equal(porNumero.Id, result.Employee!.Id);
    }

    [Theory]
    [InlineData("0114", "114")]
    [InlineData("114", "0114")]
    [InlineData("007", "7")]
    public void NumeroYPin_IgnoranCerosAIzquierda(string employeeNumber, string pin)
    {
        var emp = Emp(employeeNumber, "Roberto Diaz");

        var result = Single(User(pin, "otro nombre"), [emp]);

        Assert.True(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.EmployeeNumber, result.Kind);
    }

    [Fact]
    public void NombreTruncadoPorElReloj_VinculaSiEsElUnicoQueEmpiezaAsi()
    {
        var full = Emp("20", "Maria Guadalupe Hernandez Ramirez");

        // El reloj corta el nombre a ~24 caracteres.
        var result = Single(User("9", "Maria Guadalupe Hernand"), [full, Emp("21", "Ana Torres")]);

        Assert.True(result.ShouldLink);
        Assert.Equal(full.Id, result.Employee!.Id);
        Assert.Equal(DeviceUserMatchKind.TruncatedName, result.Kind);
    }

    [Fact]
    public void NombreCortoNoCoincidePorPrefijo()
    {
        // "Ana" es prefijo de "Ana Torres" pero es demasiado corto para arriesgar un vínculo.
        var result = Single(User("9", "Ana"), [Emp("21", "Ana Torres")]);

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.NoMatch, result.Kind);
    }

    [Fact]
    public void PrefijoAmbiguo_NoAdivina()
    {
        var a = Emp("30", "Maria Guadalupe Hernandez Ramirez");
        var b = Emp("31", "Maria Guadalupe Hernandez Soto");

        var result = Single(User("9", "Maria Guadalupe Hernand"), [a, b]);

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.Ambiguous, result.Kind);
    }

    [Fact]
    public void Homonimos_DesempataElNumeroDeEmpleadoIgualAlPin()
    {
        var first = Emp("40", "Juan Lopez");
        var second = Emp("41", "Juan Lopez");

        var result = Single(User("41", "Juan Lopez"), [first, second]);

        Assert.True(result.ShouldLink);
        Assert.Equal(second.Id, result.Employee!.Id);
    }

    [Fact]
    public void Homonimos_SinDesempate_NoAdivina()
    {
        var result = Single(User("99", "Juan Lopez"), [Emp("40", "Juan Lopez"), Emp("41", "Juan Lopez")]);

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.Ambiguous, result.Kind);
    }

    [Fact]
    public void SinCoincidenciaDeNombre_CaeAlNumeroDeEmpleadoIgualAlPin()
    {
        var emp = Emp("55", "Roberto Diaz");

        var result = Single(User("55", "R. Diaz"), [emp]);

        Assert.True(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.EmployeeNumber, result.Kind);
    }

    [Fact]
    public void NingunaCoincidencia_QuedaParaVincularAMano()
    {
        var result = Single(User("77", "Nadie Conocido"), [Emp("1", "Ana Torres")]);

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.NoMatch, result.Kind);
    }

    [Fact]
    public void PinYaVinculado_NoSeToca()
    {
        var emp = Emp("12", "Jose Perez");

        var result = Single(User("7", "Jose Perez"), [emp], linkedPins: new HashSet<string> { "7" });

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.AlreadyLinked, result.Kind);
    }

    [Fact]
    public void EmpleadoYaVinculadoAOtroPin_NoSeVinculaDeNuevo()
    {
        var emp = Emp("12", "Jose Perez");

        var result = Single(User("8", "Jose Perez"), [emp], linkedEmployees: new HashSet<Guid> { emp.Id });

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.EmployeeAlreadyLinked, result.Kind);
    }

    [Fact]
    public void DosUsuariosDelRelojParaElMismoEmpleado_SoloElPrimeroVincula()
    {
        var emp = Emp("12", "Jose Perez");

        var results = DeviceUserEmployeeMatcher.Match(
            [User("7", "Jose Perez"), User("8", "Jose Perez")], [emp], NoPins, NoEmployees);

        Assert.True(results[0].ShouldLink);
        Assert.False(results[1].ShouldLink);
        Assert.Equal(DeviceUserMatchKind.EmployeeAlreadyLinked, results[1].Kind);
    }

    [Fact]
    public void EmpleadoDadoDeBaja_NoSeVincula()
    {
        var baja = Emp("12", "Jose Perez", EmploymentStatus.Terminated);

        var result = Single(User("7", "Jose Perez"), [baja]);

        Assert.False(result.ShouldLink);
        Assert.Equal(DeviceUserMatchKind.NoMatch, result.Kind);
    }
}
