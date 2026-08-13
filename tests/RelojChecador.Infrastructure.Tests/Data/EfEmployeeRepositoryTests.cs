using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfEmployeeRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfEmployeeRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_LuegoGetByNumberAsync_RecuperaAlEmpleado()
    {
        var branchId = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfEmployeeRepository(context);
        var employee = Employee.Create(
            EmployeeNumber.Create("0114"), "Ana Torres", branchId, new DateOnly(2024, 3, 1));

        await repository.AddAsync(employee);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfEmployeeRepository(readContext);
        var recovered = await readRepository.GetByNumberAsync(EmployeeNumber.Create("0114"));

        Assert.NotNull(recovered);
        Assert.Equal("Ana Torres", recovered!.FullName);
        Assert.Equal(branchId, recovered.BranchId);
    }

    [Fact]
    public async Task ListByBranchAsync_SoloDevuelveEmpleadosDeEsaSucursal()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfEmployeeRepository(context);
        await repository.AddAsync(Employee.Create(EmployeeNumber.Create("A1"), "Empleado A", branchA, DateOnly.FromDateTime(DateTime.Today)));
        await repository.AddAsync(Employee.Create(EmployeeNumber.Create("B1"), "Empleado B", branchB, DateOnly.FromDateTime(DateTime.Today)));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfEmployeeRepository(readContext);
        var employeesOfA = await readRepository.ListByBranchAsync(branchA);

        Assert.All(employeesOfA, e => Assert.Equal(branchA, e.BranchId));
        Assert.Contains(employeesOfA, e => e.FullName == "Empleado A");
    }

    [Fact]
    public async Task Number_EsUnicoEnLaBaseDeDatos()
    {
        var branchId = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfEmployeeRepository(context);
        await repository.AddAsync(Employee.Create(EmployeeNumber.Create("DUP1"), "Primero", branchId, DateOnly.FromDateTime(DateTime.Today)));
        await context.SaveChangesAsync();

        using var duplicateContext = _fixture.CreateContext();
        var duplicateRepository = new EfEmployeeRepository(duplicateContext);
        await duplicateRepository.AddAsync(Employee.Create(EmployeeNumber.Create("DUP1"), "Segundo", branchId, DateOnly.FromDateTime(DateTime.Today)));

        await Assert.ThrowsAnyAsync<Exception>(() => duplicateContext.SaveChangesAsync());
    }
}
