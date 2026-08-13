using RelojChecador.Domain.Branches;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfBranchRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfBranchRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_LuegoGetByIdAsync_RecuperaLaMismaSucursal()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfBranchRepository(context);
        var branch = Branch.Create("norte", "Sucursal Norte", "America/Tijuana", "Carwash SA de CV");

        await repository.AddAsync(branch);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfBranchRepository(readContext);
        var recovered = await readRepository.GetByIdAsync(branch.Id);

        Assert.NotNull(recovered);
        Assert.Equal("NORTE", recovered!.Code);
        Assert.Equal("Sucursal Norte", recovered.Name);
    }

    [Fact]
    public async Task GetByCodeAsync_EsInsensibleAMayusculas()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfBranchRepository(context);
        await repository.AddAsync(Branch.Create("centro", "Sucursal Centro", "America/Tijuana"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfBranchRepository(readContext);
        var recovered = await readRepository.GetByCodeAsync("centro");

        Assert.NotNull(recovered);
        Assert.Equal("Sucursal Centro", recovered!.Name);
    }

    [Fact]
    public async Task ListAsync_DevuelveTodasOrdenadasPorNombre()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfBranchRepository(context);
        await repository.AddAsync(Branch.Create("z", "Zona Sur", "America/Tijuana"));
        await repository.AddAsync(Branch.Create("a", "Área Norte", "America/Tijuana"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfBranchRepository(readContext);
        var all = await readRepository.ListAsync();

        Assert.True(all.Count >= 2);
        var names = all.Select(b => b.Name).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.Ordinal), names);
    }
}
