using RelojChecador.Domain.Devices;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Tests.Data;

public class EfDeviceRepositoryTests : IClassFixture<SqliteInMemoryFixture>
{
    private readonly SqliteInMemoryFixture _fixture;

    public EfDeviceRepositoryTests(SqliteInMemoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_LuegoGetByIdAsync_ConservaCapacidadesYEstado()
    {
        var branchId = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfDeviceRepository(context);
        var device = Device.Register(
            "Entrada Principal", "ZKTeco", "F22/ID", "192.168.1.201", 4370, branchId, "America/Tijuana",
            serialNumber: "CQZ7233360308", macAddress: "00:17:61:13:19:dc");
        device.UpdateCapabilities(DeviceCapabilities.DownloadAttendanceLogs | DeviceCapabilities.DownloadUsers);
        device.RecordSuccessfulCommunication(DateTime.UtcNow);

        await repository.AddAsync(device);
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfDeviceRepository(readContext);
        var recovered = await readRepository.GetByIdAsync(device.Id);

        Assert.NotNull(recovered);
        Assert.Equal("CQZ7233360308", recovered!.SerialNumber);
        Assert.Equal(DeviceStatus.Online, recovered.Status);
        Assert.True(recovered.Capabilities.HasFlag(DeviceCapabilities.DownloadAttendanceLogs));
    }

    [Fact]
    public async Task ListByBranchAsync_FiltraPorSucursal()
    {
        var branchA = Guid.NewGuid();
        var branchB = Guid.NewGuid();
        using var context = _fixture.CreateContext();
        var repository = new EfDeviceRepository(context);
        await repository.AddAsync(Device.Register("Reloj A", "ZKTeco", "F22/ID", "192.168.1.10", 4370, branchA, "America/Tijuana"));
        await repository.AddAsync(Device.Register("Reloj B", "ZKTeco", "F22/ID", "192.168.1.11", 4370, branchB, "America/Tijuana"));
        await context.SaveChangesAsync();

        using var readContext = _fixture.CreateContext();
        var readRepository = new EfDeviceRepository(readContext);
        var devicesOfA = await readRepository.ListByBranchAsync(branchA);

        Assert.All(devicesOfA, d => Assert.Equal(branchA, d.BranchId));
        Assert.Contains(devicesOfA, d => d.Name == "Reloj A");
    }
}
