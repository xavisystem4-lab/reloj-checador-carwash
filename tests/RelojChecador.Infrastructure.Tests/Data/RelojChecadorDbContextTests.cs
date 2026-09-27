using Microsoft.EntityFrameworkCore;
using RelojChecador.Domain.EmployeeDeviceMappings;
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

    /// <summary>Lo que hace "Reemplazar catálogo" al unir registros: en UN solo guardado se quita
    /// el PIN "de relleno" de alguien y se le pasa el PIN real que era de su registro viejo —
    /// ambos índices únicos (reloj, empleado) y (reloj, PIN) deben respetarse.</summary>
    [Fact]
    public async Task QuitarPinDeRelleno_YReasignarPinReal_EnUnSoloGuardado()
    {
        var deviceId = Guid.NewGuid();
        var current = Emp("MAP-1", "Antony Beltran");
        var old = Emp("MAP-2", "Antony Salvador Beltran Garcia");
        var placeholder = EmployeeDeviceMapping.Create(current.Id, deviceId, "69");
        var real = EmployeeDeviceMapping.Create(old.Id, deviceId, "9");

        using (var setup = _fixture.CreateContext())
        {
            setup.AddRange(current, old, placeholder, real);
            await setup.SaveChangesAsync();
        }

        using (var context = _fixture.CreateContext())
        {
            var repository = new EfEmployeeDeviceMappingRepository(context);
            await repository.RemoveAsync((await repository.GetByIdAsync(placeholder.Id))!);
            (await repository.GetByIdAsync(real.Id))!.ReassignEmployee(current.Id);
            await context.SaveChangesAsync();
        }

        using var readContext = _fixture.CreateContext();
        var mappings = (await new EfEmployeeDeviceMappingRepository(readContext).ListAsync()).Where(m => m.DeviceId == deviceId).ToList();
        var mapping = Assert.Single(mappings);
        Assert.Equal(current.Id, mapping.EmployeeId);
        Assert.Equal("9", mapping.DeviceUserPin);
    }
}
