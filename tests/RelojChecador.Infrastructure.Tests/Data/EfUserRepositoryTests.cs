using RelojChecador.Domain.Identity;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfUserRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfUserRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_LuegoGetByUsernameAsync_ConservaLasSucursalesAsignadas()
    {
        var branchNorte = Guid.NewGuid();
        var branchSur = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfUserRepository(context);
        var user = User.Create("supervisor.norte", RoleName.Supervisor);
        user.GrantBranchAccess(branchNorte);
        user.GrantBranchAccess(branchSur);

        await repository.AddAsync(user);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfUserRepository(readContext);
        var recovered = await readRepository.GetByUsernameAsync("supervisor.norte");

        Assert.NotNull(recovered);
        Assert.Equal(2, recovered!.BranchIds.Count);
        Assert.Contains(branchNorte, recovered.BranchIds);
        Assert.Contains(branchSur, recovered.BranchIds);
    }

    [Fact]
    public async Task AddAsync_UsuarioSinSucursalesAsignadas_RecuperaListaVacia()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfUserRepository(context);
        var admin = User.Create("admin.general", RoleName.Administrador);

        await repository.AddAsync(admin);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfUserRepository(readContext);
        var recovered = await readRepository.GetByUsernameAsync("admin.general");

        Assert.NotNull(recovered);
        Assert.Empty(recovered!.BranchIds);
        Assert.True(recovered.HasAccessToBranch(Guid.NewGuid())); // Administrador: acceso implícito
    }
}
