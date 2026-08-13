using RelojChecador.Domain.Identity;

namespace RelojChecador.Domain.Tests.Identity;

public class UserTests
{
    [Fact]
    public void Create_NormalizaUsernameAMinusculas()
    {
        var user = User.Create("  Xavi.Admin  ", RoleName.Administrador);

        Assert.Equal("xavi.admin", user.Username);
        Assert.True(user.IsActive);
    }

    [Fact]
    public void HasAccessToBranch_Administrador_TieneAccesoATodo_AunSinSucursalesAsignadas()
    {
        var admin = User.Create("admin", RoleName.Administrador);

        Assert.True(admin.HasAccessToBranch(Guid.NewGuid()));
    }

    [Fact]
    public void HasAccessToBranch_RolNoAdministrador_SoloAccedeASucursalesAsignadas()
    {
        var supervisor = User.Create("supervisor.norte", RoleName.Supervisor);
        var branchNorte = Guid.NewGuid();
        var branchSur = Guid.NewGuid();

        supervisor.GrantBranchAccess(branchNorte);

        Assert.True(supervisor.HasAccessToBranch(branchNorte));
        Assert.False(supervisor.HasAccessToBranch(branchSur));
    }

    [Fact]
    public void RevokeBranchAccess_QuitaElAccesoPreviamenteOtorgado()
    {
        var user = User.Create("rh.centro", RoleName.RecursosHumanos);
        var branchId = Guid.NewGuid();
        user.GrantBranchAccess(branchId);

        user.RevokeBranchAccess(branchId);

        Assert.False(user.HasAccessToBranch(branchId));
    }

    [Fact]
    public void Deactivate_MarcaElUsuarioComoInactivo()
    {
        var user = User.Create("auditor1", RoleName.Auditor);

        user.Deactivate();

        Assert.False(user.IsActive);
    }
}
