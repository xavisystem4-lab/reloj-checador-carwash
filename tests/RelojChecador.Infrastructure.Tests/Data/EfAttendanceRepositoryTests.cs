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

    [Fact]
    public async Task ListAsync_IgnoraElFiltroDeSucursalYRespetaElTope()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        var deviceId = Guid.NewGuid();
        // Año 2031, exclusivo de este test: ListAsync no filtra por dispositivo/sucursal
        // (a diferencia del resto de los tests de esta clase), así que un rango de fechas
        // compartido con otro test recogería también sus datos — SqliteInMemoryFixture
        // mantiene la misma base para toda la clase (ver su comentario de clase).
        var masReciente = new DateTime(2031, 1, 13, 9, 0, 0, DateTimeKind.Utc);
        var masAntiguaDentroDelRango = new DateTime(2031, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        var fueraDelRango = new DateTime(2031, 1, 1, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        // "2" (branchB) es la más reciente dentro del rango — confirma que ListAsync no
        // filtra por sucursal, solo por fecha, y que Take(maxCount) respeta el orden
        // descendente en vez de recortar de forma arbitraria.
        await repository.AddAsync(Attendance.Create(
            deviceId, branchA, "1", masAntiguaDentroDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await repository.AddAsync(Attendance.Create(
            deviceId, branchB, "2", masReciente, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await repository.AddAsync(Attendance.Create(
            deviceId, branchA, "3", fueraDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var results = await readRepository.ListAsync(
            new DateTime(2031, 1, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2031, 1, 14, 0, 0, 0, DateTimeKind.Utc), maxCount: 1);

        Assert.Single(results);
        Assert.Equal("2", results[0].DeviceUserPin);
    }

    [Fact]
    public async Task ListUnresolvedByDeviceAndPinAsync_SoloTraeMarcacionesSinEmpleadoVinculado()
    {
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        var timestamp1 = new DateTime(2026, 8, 13, 8, 0, 0, DateTimeKind.Utc);
        var timestamp2 = new DateTime(2026, 8, 13, 9, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var sinResolver = Attendance.Create(
            deviceId, branchId, "5", timestamp1, AttendanceVerifyMethod.Fingerprint, 0, "raw");
        var yaResuelta = Attendance.Create(
            deviceId, branchId, "5", timestamp2, AttendanceVerifyMethod.Fingerprint, 0, "raw", employeeId: employeeId);
        var otroPin = Attendance.Create(
            deviceId, branchId, "6", timestamp1, AttendanceVerifyMethod.Fingerprint, 0, "raw");
        await repository.AddAsync(sinResolver);
        await repository.AddAsync(yaResuelta);
        await repository.AddAsync(otroPin);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var results = await readRepository.ListUnresolvedByDeviceAndPinAsync(deviceId, "5");

        Assert.Single(results);
        Assert.Equal(sinResolver.Id, results[0].Id);
    }

    [Fact]
    public async Task ListByEmployeeInRangeAsync_FiltraPorEmpleadoYRango()
    {
        // Año 2032, exclusivo de este test — mismo criterio que ListAsync_..., la fixture
        // comparte base entre todos los tests de la clase.
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var empleadoA = Guid.NewGuid();
        var empleadoB = Guid.NewGuid();
        var dentroDelRango = new DateTime(2032, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        var fueraDelRango = new DateTime(2032, 1, 14, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var esperada = Attendance.Create(
            deviceId, branchId, "1", dentroDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw", employeeId: empleadoA);
        await repository.AddAsync(esperada);
        await repository.AddAsync(Attendance.Create(
            deviceId, branchId, "2", dentroDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw", employeeId: empleadoB));
        await repository.AddAsync(Attendance.Create(
            deviceId, branchId, "1", fueraDelRango, AttendanceVerifyMethod.Fingerprint, 0, "raw", employeeId: empleadoA));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var results = await readRepository.ListByEmployeeInRangeAsync(
            empleadoA, new DateTime(2032, 1, 13, 0, 0, 0, DateTimeKind.Utc), new DateTime(2032, 1, 14, 0, 0, 0, DateTimeKind.Utc));

        Assert.Single(results);
        Assert.Equal(esperada.Id, results[0].Id);
    }

    [Fact]
    public async Task SetAllPunchTypesAsync_CambiaTodasLasFilasYActualizaUpdatedAtUtc()
    {
        // Año 2033, exclusivo de este test.
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2033, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var conTipoDistinto = Attendance.Create(
            deviceId, branchId, "1", timestamp, AttendanceVerifyMethod.Fingerprint, 5, "raw");
        var sinTipo = Attendance.Create(
            deviceId, branchId, "2", timestamp, AttendanceVerifyMethod.Fingerprint, null, "raw");
        var updatedAtOriginal = conTipoDistinto.UpdatedAtUtc;
        await repository.AddAsync(conTipoDistinto);
        await repository.AddAsync(sinTipo);
        await context.SaveChangesAsync();

        using var writeContext = _fixture.CreateContext();
        var writeRepository = new EfAttendanceRepository(writeContext);
        var affected = await writeRepository.SetAllPunchTypesAsync(0);

        // >= 2 porque otros tests de esta clase también insertan filas en la misma base
        // compartida — lo que importa es que AL MENOS las dos de este test se hayan tocado.
        Assert.True(affected >= 2);

        using var readContext = _fixture.CreateContext();
        var reloaded = await readContext.Attendances
            .Where(a => a.Id == conTipoDistinto.Id || a.Id == sinTipo.Id)
            .ToListAsync();

        Assert.All(reloaded, a => Assert.Equal(0, a.PunchType));
        Assert.All(reloaded, a => Assert.True(a.UpdatedAtUtc > updatedAtOriginal));
    }

    [Fact]
    public async Task GetByIdAsync_ConIdExistente_LaEncuentra()
    {
        // Año 2034, exclusivo de este test.
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2034, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var attendance = Attendance.Create(deviceId, branchId, "1", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "raw");
        await repository.AddAsync(attendance);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var found = await readRepository.GetByIdAsync(attendance.Id);
        var notFound = await readRepository.GetByIdAsync(Guid.NewGuid());

        Assert.NotNull(found);
        Assert.Equal(attendance.Id, found!.Id);
        Assert.Null(notFound);
    }

    [Fact]
    public async Task EditByAdmin_LuegoGuardado_PersisteTipoYNota()
    {
        // Año 2035, exclusivo de este test — pedido explícito del usuario: "que las
        // asistencias se puedan editar ... y pueda colocarle si es entrada o salida ...
        // nota en especial también".
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2035, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var attendance = Attendance.Create(deviceId, branchId, "1", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "raw");
        await repository.AddAsync(attendance);
        await context.SaveChangesAsync();

        using var writeContext = _fixture.CreateContext();
        var writeRepository = new EfAttendanceRepository(writeContext);
        var reloadedForEdit = await writeRepository.GetByIdAsync(attendance.Id);
        reloadedForEdit!.EditByAdmin(1, "  Salió temprano por permiso  ");
        await writeContext.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(attendance.Id);

        Assert.Equal(1, reloaded!.PunchType);
        Assert.Equal("Salió temprano por permiso", reloaded.Notes);
    }

    [Fact]
    public async Task RemoveAsync_LuegoGuardado_LaBorraDeVerdad()
    {
        // Año 2036, exclusivo de este test — pedido explícito del usuario: "o eliminar
        // Marcación".
        var deviceId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var timestamp = new DateTime(2036, 1, 13, 8, 0, 0, DateTimeKind.Utc);
        using var context = _fixture.CreateContext();
        var repository = new EfAttendanceRepository(context);
        var attendance = Attendance.Create(deviceId, branchId, "1", timestamp, AttendanceVerifyMethod.Fingerprint, 0, "raw");
        await repository.AddAsync(attendance);
        await context.SaveChangesAsync();

        using var writeContext = _fixture.CreateContext();
        var writeRepository = new EfAttendanceRepository(writeContext);
        var toDelete = await writeRepository.GetByIdAsync(attendance.Id);
        await writeRepository.RemoveAsync(toDelete!);
        await writeContext.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfAttendanceRepository(readContext);
        var reloaded = await readRepository.GetByIdAsync(attendance.Id);

        Assert.Null(reloaded);
    }
}
