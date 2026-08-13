using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Tests.Branches;

public class BranchTests
{
    [Fact]
    public void Create_ConValoresValidos_AsignaCampos()
    {
        var branch = Branch.Create("norte", "Sucursal Norte", "America/Tijuana", "Carwash SA de CV", "Av. Reforma 123");

        Assert.Equal("NORTE", branch.Code); // se normaliza a mayúsculas
        Assert.Equal("Sucursal Norte", branch.Name);
        Assert.Equal("America/Tijuana", branch.TimeZoneId);
        Assert.True(branch.IsActive);
        Assert.NotEqual(Guid.Empty, branch.Id);
        Assert.Equal(branch.CreatedAtUtc, branch.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("", "Sucursal Norte")]
    [InlineData("norte", "")]
    [InlineData(" ", "Sucursal Norte")]
    public void Create_ConCamposRequeridosVacios_LanzaDomainException(string code, string name)
    {
        Assert.Throws<DomainException>(() => Branch.Create(code, name, "America/Tijuana"));
    }

    [Fact]
    public void Deactivate_MarcaComoInactivaYActualizaTimestamp()
    {
        var branch = Branch.Create("norte", "Sucursal Norte", "America/Tijuana");
        var updatedAtOriginal = branch.UpdatedAtUtc;

        branch.Deactivate();

        Assert.False(branch.IsActive);
        Assert.True(branch.UpdatedAtUtc >= updatedAtOriginal);
    }

    [Fact]
    public void AssignManager_PermiteAsignarYQuitarResponsable()
    {
        var branch = Branch.Create("norte", "Sucursal Norte", "America/Tijuana");
        var managerId = Guid.NewGuid();

        branch.AssignManager(managerId);
        Assert.Equal(managerId, branch.ManagerEmployeeId);

        branch.AssignManager(null);
        Assert.Null(branch.ManagerEmployeeId);
    }
}
