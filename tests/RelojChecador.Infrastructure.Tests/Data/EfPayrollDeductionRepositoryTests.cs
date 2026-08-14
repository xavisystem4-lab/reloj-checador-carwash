using Microsoft.EntityFrameworkCore;
using RelojChecador.Domain.Payroll;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfPayrollDeductionRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfPayrollDeductionRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    private static DateOnly WeekOf(int day) => new(2027, 1, day); // año exclusivo de este archivo, evita colisión con datos de otros tests que comparten la conexión :memory:

    [Fact]
    public async Task AddAsync_LuegoGetByEmployeeAndWeekAsync_RecuperaLaFilaCorrecta()
    {
        var employeeId = Guid.NewGuid();
        var weekStart = WeekOf(4);
        using var context = _fixture.CreateContext();
        var repository = new EfPayrollDeductionRepository(context);
        var deduction = PayrollDeduction.Create(employeeId, weekStart);
        deduction.UpdateAmounts(300m, 150m, 0m, null, null);

        await repository.AddAsync(deduction);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfPayrollDeductionRepository(readContext);
        var recovered = await readRepository.GetByEmployeeAndWeekAsync(employeeId, weekStart);

        Assert.NotNull(recovered);
        Assert.Equal(300m, recovered!.IsrAmount);
        Assert.Equal(150m, recovered.ImssAmount);
    }

    [Fact]
    public async Task GetByEmployeeAndWeekAsync_SinFilaCapturada_DevuelveNull()
    {
        using var context = _fixture.CreateContext();
        var repository = new EfPayrollDeductionRepository(context);

        var recovered = await repository.GetByEmployeeAndWeekAsync(Guid.NewGuid(), WeekOf(11));

        Assert.Null(recovered);
    }

    [Fact]
    public async Task EmployeeIdYWeekStart_EsUnicoEnLaBaseDeDatos()
    {
        var employeeId = Guid.NewGuid();
        var weekStart = WeekOf(18);
        using var context = _fixture.CreateContext();
        var repository = new EfPayrollDeductionRepository(context);
        await repository.AddAsync(PayrollDeduction.Create(employeeId, weekStart));
        await context.SaveChangesAsync();

        // Simula lo que PayrollViewModel.UpdateDeductionsAsync evita en la práctica
        // (buscar-o-crear) — si de todos modos se intenta un segundo Create para el mismo
        // empleado/semana, el índice único de la migración debe rechazarlo.
        await repository.AddAsync(PayrollDeduction.Create(employeeId, weekStart));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task ListByWeekAsync_SoloDevuelveFilasDeEsaSemana()
    {
        var weekA = WeekOf(25);
        var weekB = weekA.AddDays(7);
        using var context = _fixture.CreateContext();
        var repository = new EfPayrollDeductionRepository(context);
        await repository.AddAsync(PayrollDeduction.Create(Guid.NewGuid(), weekA));
        await repository.AddAsync(PayrollDeduction.Create(Guid.NewGuid(), weekB));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfPayrollDeductionRepository(readContext);
        var rowsOfWeekA = await readRepository.ListByWeekAsync(weekA);

        Assert.All(rowsOfWeekA, d => Assert.Equal(weekA, d.WeekStart));
    }
}
