using RelojChecador.Application.Employees;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Tests.Employees;

public class EmployeeDuplicateFinderTests
{
    private static DuplicateCandidate C(string number, string name, int punches = 0, bool terminated = false, bool pin = false)
    {
        var employee = Employee.Create(EmployeeNumber.Create(number), name, Guid.NewGuid(), new DateOnly(2025, 1, 1), null);
        if (terminated)
        {
            employee.ChangeStatus(EmploymentStatus.Terminated);
        }
        return new DuplicateCandidate(employee, punches, pin);
    }

    [Fact]
    public void NombreCortoYLargo_SonElMismoGrupo_SeQuedaElVigenteConMarcaciones()
    {
        var vigente = C("EMP-001", "Adrian Uribe", punches: 120, pin: true);
        var viejo = C("1-baja", "Adrian Uribe Garcia", terminated: true);

        var group = Assert.Single(EmployeeDuplicateFinder.Find([viejo, vigente, C("EMP-002", "Angel David")]));

        Assert.Same(vigente, group.Keeper);
        Assert.Same(viejo, Assert.Single(group.Others));
        Assert.Equal("Adrian Uribe Garcia", group.SuggestedName);
        Assert.False(group.NeedsReview);
    }

    [Fact]
    public void MismoNombreConAcentosYMayusculas_EsRepetido()
    {
        var groups = EmployeeDuplicateFinder.Find([C("1", "José Peña"), C("2", "JOSE  PENA", punches: 3)]);
        Assert.Equal("2", Assert.Single(groups).Keeper.Employee.Number.Value);
    }

    [Fact]
    public void UnaSolaPalabra_NoCuentaComoParecido()
    {
        Assert.Empty(EmployeeDuplicateFinder.Find([C("1", "Luis"), C("2", "Luis Luna")]));
    }

    [Fact]
    public void NombresDistintosQueComparten_NoSeAgrupan()
    {
        Assert.Empty(EmployeeDuplicateFinder.Find([C("1", "Jesus Lopez"), C("2", "Jesus Rios")]));
    }

    [Fact]
    public void GrupoConDosPersonasPosibles_QuedaParaRevisar()
    {
        var group = Assert.Single(EmployeeDuplicateFinder.Find(
            [C("1", "Jesus Lopez", punches: 5), C("2", "Jesus Lopez Garcia"), C("3", "Jesus Lopez Ramos")]));

        Assert.True(group.NeedsReview);
        Assert.Equal("Jesus Lopez", group.SuggestedName);
    }
}
