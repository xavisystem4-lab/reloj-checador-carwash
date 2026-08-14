using RelojChecador.Domain.Common;
using RelojChecador.Domain.Devices;

namespace RelojChecador.Domain.Tests.Devices;

public class DeviceTests
{
    private static Device CreateSampleDevice(Guid? branchId = null) =>
        Device.Register(
            "Entrada Principal",
            "ZKTeco",
            "F22/ID",
            "192.168.1.201",
            4370,
            branchId ?? Guid.NewGuid(),
            "America/Tijuana",
            serialNumber: "CQZ7233360308",
            macAddress: "00:17:61:13:19:dc");

    [Fact]
    public void Register_ConValoresValidos_QuedaEnEstadoDesconocido()
    {
        var device = CreateSampleDevice();

        // Nunca se asume "conectado" por defecto: hasta no diagnosticar, el estado es Unknown.
        Assert.Equal(DeviceStatus.Unknown, device.Status);
        Assert.Equal(DeviceCapabilities.None, device.Capabilities);
        Assert.Null(device.LastCommunicationAtUtc);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void Register_ConPuertoFueraDeRango_LanzaDomainException(int invalidPort)
    {
        Assert.Throws<DomainException>(() =>
            Device.Register("Entrada", "ZKTeco", "F22/ID", "192.168.1.201", invalidPort, Guid.NewGuid(), "America/Tijuana"));
    }

    [Fact]
    public void RecordSuccessfulCommunication_ActualizaEstadoYFecha()
    {
        var device = CreateSampleDevice();
        var now = DateTime.UtcNow;

        device.RecordSuccessfulCommunication(now);

        Assert.Equal(DeviceStatus.Online, device.Status);
        Assert.Equal(now, device.LastCommunicationAtUtc);
    }

    [Fact]
    public void RecordFailedCommunication_MarcaComoOffline_SinTocarUltimaComunicacionExitosa()
    {
        var device = CreateSampleDevice();
        var successfulAt = DateTime.UtcNow;
        device.RecordSuccessfulCommunication(successfulAt);

        device.RecordFailedCommunication();

        Assert.Equal(DeviceStatus.Offline, device.Status);
        // La última comunicación EXITOSA conocida no debe perderse solo porque el intento actual falló.
        Assert.Equal(successfulAt, device.LastCommunicationAtUtc);
    }

    [Fact]
    public void UpdateCapabilities_PermiteCombinarFlags()
    {
        var device = CreateSampleDevice();

        device.UpdateCapabilities(DeviceCapabilities.DownloadAttendanceLogs | DeviceCapabilities.SetDeviceTime);

        Assert.True(device.Capabilities.HasFlag(DeviceCapabilities.DownloadAttendanceLogs));
        Assert.True(device.Capabilities.HasFlag(DeviceCapabilities.SetDeviceTime));
        Assert.False(device.Capabilities.HasFlag(DeviceCapabilities.ManageUsers));
    }

    [Fact]
    public void UpdateDetails_ConValoresValidos_ActualizaTodosLosCamposCapturablesAlAlta()
    {
        var device = CreateSampleDevice();
        var newBranchId = Guid.NewGuid();

        device.UpdateDetails("Entrada Trasera", "Zkteco", "iClock 880", newBranchId, "America/Mexico_City", "NEWSERIAL", "AA:BB:CC:DD:EE:FF");

        Assert.Equal("Entrada Trasera", device.Name);
        Assert.Equal("Zkteco", device.Brand);
        Assert.Equal("iClock 880", device.Model);
        Assert.Equal(newBranchId, device.BranchId);
        Assert.Equal("America/Mexico_City", device.TimeZoneId);
        Assert.Equal("NEWSERIAL", device.SerialNumber);
        Assert.Equal("AA:BB:CC:DD:EE:FF", device.MacAddress);
    }

    [Fact]
    public void UpdateDetails_ConNombreVacio_LanzaDomainException()
    {
        var device = CreateSampleDevice();

        Assert.Throws<DomainException>(() =>
            device.UpdateDetails("", "ZKTeco", "F22/ID", Guid.NewGuid(), "America/Tijuana", null, null));
    }

    [Fact]
    public void UpdateDetails_ConSucursalVacia_LanzaDomainException()
    {
        var device = CreateSampleDevice();

        Assert.Throws<DomainException>(() =>
            device.UpdateDetails("Entrada", "ZKTeco", "F22/ID", Guid.Empty, "America/Tijuana", null, null));
    }
}
