using Microsoft.EntityFrameworkCore;
using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class RelojChecadorDbContextTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public RelojChecadorDbContextTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static Employee Emp(string number, string name) =>
        Employee.Create(EmployeeNumber.Create(number), name, Guid.NewGuid(), new DateOnly(2025, 1, 1), weeklySalary: null);

    [Fact]
    public async Task DiscardPendingChanges_TrasUnGuardadoFallido_ElSiguienteGuardadoFunciona()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfEmployeeRepository(context);
        var ana = Emp("DPC-1", "Ana");
        var luis = Emp("DPC-2", "Luis");
        await repository.AddAsync(ana);
        await repository.AddAsync(luis);
        await context.SaveChangesAsync();

        // Número duplicado (índice único) + un alta en el mismo lote: el guardado falla.
        luis.ChangeNumber(EmployeeNumber.Create("DPC-1"));
        luis.ChangeStatus(EmploymentStatus.Terminated);
        await repository.AddAsync(Emp("DPC-3", "Pendiente"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        context.DiscardPendingChanges();

        Assert.Equal("DPC-2", luis.Number.Value);
        Assert.Equal(EmploymentStatus.Active, luis.Status);
        await repository.AddAsync(Emp("DPC-4", "Nuevo"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var numbers = (await new EfEmployeeRepository(readContext).ListAsync())
            .Select(e => e.Number.Value).Where(n => n.StartsWith("DPC-")).Order().ToList();
        Assert.Equal(["DPC-1", "DPC-2", "DPC-4"], numbers);
    }
}
