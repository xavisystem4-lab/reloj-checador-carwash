using RelojChecador.Application.Branches;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.Identity;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfBranchIdReconcilerTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfBranchIdReconcilerTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReassignAsync_CambiaElIdDeLaSucursalYDeTodoLoQueLaReferencia()
    {
        var newId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        Guid oldId;
        using (var context = _fixture.CreateContext())
        {
            var branch = Branch.Create("REASIGNA", "Cafetería", "America/Tijuana");
            oldId = branch.Id;
            context.Branches.Add(branch);
            context.Employees.Add(Employee.Create(
                EmployeeNumber.Create("7001"), "Ana Torres", oldId, new DateOnly(2024, 3, 1), weeklySalary: 2500m));
            context.Employees.Add(Employee.Create(
                EmployeeNumber.Create("7002"), "Otra Sucursal", otherBranchId, new DateOnly(2024, 3, 1), weeklySalary: 2500m));
            context.Devices.Add(Device.Register("Checador", "ZKTeco", "F22/ID", "192.168.1.201", 4370, oldId, "America/Tijuana"));
            context.Attendances.Add(Attendance.Create(
                Guid.NewGuid(), oldId, "1", DateTime.UtcNow, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
            var user = User.Create("supervisor.reasigna", RoleName.Supervisor);
            user.GrantBranchAccess(oldId);
            user.GrantBranchAccess(otherBranchId);
            context.Users.Add(user);
            await context.SaveChangesAsync();
        }

        using (var context = _fixture.CreateContext())
        {
            var result = await new EfBranchIdReconciler(context).ReassignAsync(oldId, newId);
            Assert.Equal(BranchIdReassignResult.Reassigned, result);
        }

        using var readContext = _fixture.CreateContext();
        Assert.Null(await new EfBranchRepository(readContext).GetByIdAsync(oldId));
        Assert.Equal("REASIGNA", (await new EfBranchRepository(readContext).GetByIdAsync(newId))!.Code);
        Assert.Equal(newId, (await new EfEmployeeRepository(readContext).GetByNumberAsync(EmployeeNumber.Create("7001")))!.BranchId);
        Assert.Equal(otherBranchId, (await new EfEmployeeRepository(readContext).GetByNumberAsync(EmployeeNumber.Create("7002")))!.BranchId);
        Assert.All(readContext.Devices.Where(d => d.IpAddress == "192.168.1.201"), d => Assert.Equal(newId, d.BranchId));
        Assert.All(readContext.Attendances.Where(a => a.DeviceUserPin == "1" && a.RawPayload == "raw"), a => Assert.Equal(newId, a.BranchId));

        var recoveredUser = await new EfUserRepository(readContext).GetByUsernameAsync("supervisor.reasigna");
        Assert.Contains(newId, recoveredUser!.BranchIds);
        Assert.Contains(otherBranchId, recoveredUser.BranchIds);
        Assert.DoesNotContain(oldId, recoveredUser.BranchIds);
    }

    [Fact]
    public async Task ReassignAsync_SiYaExisteOtraSucursalConElIdDestino_NoTocaNada()
    {
        using var context = _fixture.CreateContext();
        var first = Branch.Create("CHOQUE-A", "A", "America/Tijuana");
        var second = Branch.Create("CHOQUE-B", "B", "America/Tijuana");
        context.Branches.AddRange(first, second);
        await context.SaveChangesAsync();

        var result = await new EfBranchIdReconciler(context).ReassignAsync(first.Id, second.Id);

        Assert.Equal(BranchIdReassignResult.TargetAlreadyExists, result);
        using var readContext = _fixture.CreateContext();
        Assert.NotNull(await new EfBranchRepository(readContext).GetByIdAsync(first.Id));
        Assert.NotNull(await new EfBranchRepository(readContext).GetByIdAsync(second.Id));
    }

    [Fact]
    public async Task ReassignAsync_SiLaSucursalOrigenNoExiste_DevuelveSourceNotFound()
    {
        using var context = _fixture.CreateContext();

        var result = await new EfBranchIdReconciler(context).ReassignAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(BranchIdReassignResult.SourceNotFound, result);
    }
}
