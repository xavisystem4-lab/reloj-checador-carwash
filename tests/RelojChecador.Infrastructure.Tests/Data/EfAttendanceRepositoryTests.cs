using Microsoft.EntityFrameworkCore;
using RelojChecador.Domain.Attendances;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfAttendanceRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfAttendanceRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_LuegoExistsAsync_DetectaLaMismaMarcacion()
    {
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 8, 13, 8, 2, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var attendance = Attendance.Create(
            deviceId, branchId, "7", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "ZK|7|1|0|raw");

        await repository.AddAsync(attendance);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var exists = await readRepository.ExistsAsync(deviceId, "7", timestamp);
        var existsDistinto = await readRepository.ExistsAsync(deviceId, "8", timestamp);

        Assert.True(exists);
        Assert.False(existsDistinto);
    }

    [Fact]
    public async Task AddAsync_ConMismaCombinacionDeduplicacion_ViolaElIndiceUnico()
    {
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 8, 13, 8, 2, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        await repository.AddAsync(Attendance.Create(
            deviceId, branchId, "7", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "raw-1"));
        await context.SaveChangesAsync();

        // Simula la carrera entre el sondeo en tiempo real y una descarga manual: el
        // ExistsAsync previo no es la única defensa, el índice único de la base de datos
        // es la garantía real (ver AttendanceConfiguration).
        using var raceContext = _fixture.CreateContext();
        var raceRepository = new EfAttendanceRepository(raceContext);
        await raceRepository.AddAsync(Attendance.Create(
            deviceId, branchId, "7", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "raw-2"));

        await Assert.ThrowsAsync<DbUpdateException>(() => raceContext.SaveChangesAsync());
    }

    [Fact]
    public async Task ListByBranchAsync_FiltraPorSucursalYRangoDeFechas()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        var dentroDelRango = new DateTime(2026, 8, 13, 8, 0, 0, DateTimeKind.Utc);
        var fueraDelRango = new DateTime(2026, 8, 1, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        await repository.AddAsync(Attendance.Create(
            deviceId, branchA, "1", dentroDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await repository.AddAsync(Attendance.Create(
            deviceId, branchA, "2", fueraDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await repository.AddAsync(Attendance.Create(
            deviceId, branchB, "3", dentroDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var results = await readRepository.ListByBranchAsync(
            branchA, new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc));

        Assert.Single(results);
        Assert.Equal("1", results[0].DeviceUserPin);
    }
}
