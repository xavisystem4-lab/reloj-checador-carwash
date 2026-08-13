using RelojChecador.Application.Devices;
using RelojChecador.Domain.Devices;
using RelojChecador.Infrastructure.Devices.Simulator;

namespace RelojChecador.Infrastructure.Tests.Devices;

public class SimulatorDeviceAdapterTests
{
    private static DeviceConnectionInfo SampleConnection() => new("192.168.1.201", 4370);

    [Fact]
    public async Task TestNetworkAsync_ConIpInvalida_DevuelveFallo()
    {
        var adapter = new SimulatorDeviceAdapter();

        var result = await adapter.TestNetworkAsync("no-es-una-ip");

        Assert.True(result.IsFailure);
        Assert.Equal("Device.InvalidIpAddress", result.Error.Code);
    }

    [Fact]
    public async Task TestNetworkAsync_PorDefecto_EsAlcanzable()
    {
        var adapter = new SimulatorDeviceAdapter();

        var result = await adapter.TestNetworkAsync("192.168.1.201");

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsReachable);
    }

    [Fact]
    public async Task TestNetworkAsync_SimulandoRedInalcanzable_ReportaNoAlcanzable()
    {
        var adapter = new SimulatorDeviceAdapter { SimulateNetworkUnreachable = true };

        var result = await adapter.TestNetworkAsync("192.168.1.201");

        // El propio test de red no falla como Result (la llamada se hizo con éxito),
        // pero el contenido indica que no hubo respuesta — así se distingue "no pude
        // ejecutar la prueba" de "ejecuté la prueba y no respondió".
        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsReachable);
    }

    [Fact]
    public async Task OperacionesQueRequierenConexion_SinConectarPrimero_DevuelvenNotConnected()
    {
        var adapter = new SimulatorDeviceAdapter();

        var result = await adapter.GetDeviceInformationAsync();

        Assert.True(result.IsFailure);
        Assert.Equal("Device.NotConnected", result.Error.Code);
    }

    [Fact]
    public async Task ConnectAsync_ConParametrosPorDefecto_TieneExito()
    {
        var adapter = new SimulatorDeviceAdapter();

        var result = await adapter.ConnectAsync(SampleConnection());

        Assert.True(result.IsSuccess);
        Assert.True(adapter.IsConnected);
    }

    [Fact]
    public async Task ConnectAsync_SimulandoFalloDeAutenticacion_DevuelveError()
    {
        var adapter = new SimulatorDeviceAdapter { SimulateAuthenticationFailure = true };

        var result = await adapter.ConnectAsync(SampleConnection());

        Assert.True(result.IsFailure);
        Assert.Equal("Device.AuthenticationFailed", result.Error.Code);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task GetDeviceInformationAsync_Conectado_DevuelveDatosDelF22ID()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());

        var result = await adapter.GetDeviceInformationAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("CQZ7233360308", result.Value.SerialNumber);
        Assert.Equal("ZLM60_TFT", result.Value.Platform);
    }

    [Fact]
    public async Task DownloadAttendanceLogsAsync_Conectado_DevuelveRegistrosDeSemilla()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());

        var result = await adapter.DownloadAttendanceLogsAsync();

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
    }

    [Fact]
    public async Task CreateOrUpdateUserAsync_LuegoDownloadUsers_ReflejaElCambio()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());
        var nuevoUsuario = new DeviceUserRecord("99", "Empleado Nuevo", PrivilegeLevel: 0, IsEnabled: true);

        await adapter.CreateOrUpdateUserAsync(nuevoUsuario);
        var result = await adapter.DownloadUsersAsync();

        Assert.Contains(result.Value, u => u.DeviceUserPin == "99" && u.Name == "Empleado Nuevo");
    }

    [Fact]
    public async Task DeleteUserAsync_ConPinInexistente_DevuelveUserNotFound()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());

        var result = await adapter.DeleteUserAsync("no-existe");

        Assert.True(result.IsFailure);
        Assert.Equal("Device.UserNotFound", result.Error.Code);
    }

    [Fact]
    public async Task ClearAttendanceLogsAsync_Conectado_VaciaLosRegistros()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());

        await adapter.ClearAttendanceLogsAsync();
        var result = await adapter.DownloadAttendanceLogsAsync();

        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task DisableDeviceAsync_Conectado_CambiaIsEnabled()
    {
        var adapter = new SimulatorDeviceAdapter();
        await adapter.ConnectAsync(SampleConnection());

        await adapter.DisableDeviceAsync();

        Assert.False(adapter.IsEnabled);
    }

    [Fact]
    public async Task GetSupportedCapabilitiesAsync_DevuelveTodasLasCapacidades()
    {
        var adapter = new SimulatorDeviceAdapter();

        var capabilities = await adapter.GetSupportedCapabilitiesAsync();

        Assert.True(capabilities.HasFlag(DeviceCapabilities.DownloadAttendanceLogs));
        Assert.True(capabilities.HasFlag(DeviceCapabilities.ManageUsers));
    }
}
