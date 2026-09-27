using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Employees;

/// <summary>Casos tomados de los datos reales (Supabase, 27/09/2026) — ver comentario de clase
/// de <see cref="EmployeeCatalogPinPlanner"/>.</summary>
public class EmployeeCatalogPinPlannerTests
{
    private static Employee Emp(string number, string name, bool terminated = false)
    {
        var employee = Employee.Create(EmployeeNumber.Create(number), name, Guid.NewGuid(), new DateOnly(2025, 1, 1), null);
        if (terminated)
        {
            employee.ChangeStatus(EmploymentStatus.Terminated);
        }
        return employee;
    }

    private static CatalogPinRow Row(int index, string pin, string name) => new(index, pin, name, pin);

    [Fact]
    public void VigenteConElPin_SeQueda_YAbsorbeAlRegistroViejoDelMismoNombre()
    {
        var current = Emp("EMP-001", "Adrian Uribe");
        var old = Emp("1", "Adrian Uribe Garcia", terminated: true);

        var plan = Assert.Single(EmployeeCatalogPinPlanner.Plan(
            [Row(0, "1", "Adrian Uribe Garcia")], [current, old],
            new Dictionary<string, Guid> { ["1"] = current.Id }, new HashSet<string> { "1" }));

        Assert.Equal(CatalogPinResolution.Existing, plan.Resolution);
        Assert.Same(current, plan.Survivor);
        Assert.Same(old, Assert.Single(plan.Absorbed));
    }

    [Fact]
    public void DuenoDelPinDadoDeBaja_SinVigente_SeReactivaAunqueElNombreTengaOtraOrtografia()
    {
        var old = Emp("21", "Cristoper Ivan", terminated: true);

        var plan = Assert.Single(EmployeeCatalogPinPlanner.Plan(
            [Row(0, "21", "Cristopher Ivan Matinez Ochoa")], [old, Emp("EMP-002", "Angel David")],
            new Dictionary<string, Guid> { ["21"] = old.Id }, new HashSet<string> { "21" }));

        Assert.Equal(CatalogPinResolution.Reactivate, plan.Resolution);
        Assert.Same(old, plan.Survivor);
        Assert.Empty(plan.Absorbed);
    }

    [Fact]
    public void VigenteConPinDeRelleno_SeEmparejaPorNombreConSuPinReal()
    {
        var current = Emp("EMP-010", "Antony Beltran");
        var old = Emp("9", "Antony Salvador Beltran Garcia", terminated: true);

        var plan = Assert.Single(EmployeeCatalogPinPlanner.Plan(
            [Row(0, "9", "Antony Salvador Beltran Garcia")], [current, old],
            new Dictionary<string, Guid> { ["69"] = current.Id }, new HashSet<string>()));

        Assert.Equal(CatalogPinResolution.Existing, plan.Resolution);
        Assert.Same(current, plan.Survivor);
        Assert.Same(old, Assert.Single(plan.Absorbed));
    }

    [Fact]
    public void ListaDelReloj_ConPinDeRellenoYPinReal_OmiteElDeRelleno()
    {
        var current = Emp("EMP-010", "Antony Beltran");

        var plans = EmployeeCatalogPinPlanner.Plan(
            [Row(0, "9", "Antony Salvador Beltran Garcia"), Row(1, "69", "Antony Beltran")], [current],
            new Dictionary<string, Guid> { ["69"] = current.Id }, new HashSet<string>());

        Assert.Same(current, plans[0].Survivor);
        Assert.Equal(CatalogPinResolution.SkipDuplicate, plans[1].Resolution);
        Assert.Equal(0, plans[1].DuplicateOfIndex);
    }

    [Fact]
    public void SinNadieQueCorresponda_SeCrea()
    {
        var plan = Assert.Single(EmployeeCatalogPinPlanner.Plan(
            [Row(0, "45", "Maria Fernanda De La Cruz Sanchez")], [Emp("EMP-002", "Angel David")],
            new Dictionary<string, Guid>(), new HashSet<string>()));

        Assert.Equal(CatalogPinResolution.Create, plan.Resolution);
        Assert.Null(plan.Survivor);
    }

    [Fact]
    public void DadoDeBajaSinPin_ConElMismoNombre_SeReactiva()
    {
        var old = Emp("10", "Jorge Luis Rodriguez Algandar", terminated: true);

        var plan = Assert.Single(EmployeeCatalogPinPlanner.Plan(
            [Row(0, "10", "Jorge Luis Rodriguez Algandar")], [old],
            new Dictionary<string, Guid>(), new HashSet<string>()));

        Assert.Equal(CatalogPinResolution.Reactivate, plan.Resolution);
        Assert.Same(old, plan.Survivor);
    }

    [Fact]
    public void UnRegistroViejo_SoloLoAbsorbeUnaFila()
    {
        var a = Emp("EMP-001", "Luis Luna");
        var b = Emp("EMP-002", "Luis Alfonso");
        var old = Emp("32", "Luis Fernado Luna Rojas", terminated: true);

        var plans = EmployeeCatalogPinPlanner.Plan(
            [Row(0, "32", "Luis Fernado Luna Rojas"), Row(1, "33", "Luis Alfonso Soto Trujillo")], [a, b, old],
            new Dictionary<string, Guid> { ["32"] = a.Id, ["33"] = b.Id }, new HashSet<string> { "32", "33" });

        Assert.Same(old, Assert.Single(plans[0].Absorbed));
        Assert.Empty(plans[1].Absorbed);
    }
}
