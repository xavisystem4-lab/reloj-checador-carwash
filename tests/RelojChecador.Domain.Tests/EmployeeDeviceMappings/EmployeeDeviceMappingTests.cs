using RelojChecador.Domain.Common;
using RelojChecador.Domain.EmployeeDeviceMappings;

namespace RelojChecador.Domain.Tests.EmployeeDeviceMappings;

public class EmployeeDeviceMappingTests
{
    [Fact]
    public void Create_ConValoresValidos_AsignaCampos()
    {
        var employeeId = Guid.NewGuid();
        var deviceId = Guid.NewGuid();

        var mapping = EmployeeDeviceMapping.Create(employeeId, deviceId, "37");

        Assert.Equal(employeeId, mapping.EmployeeId);
        Assert.Equal(deviceId, mapping.DeviceId);
        // El PIN del dispositivo es independiente del número de empleado del negocio.
        Assert.Equal("37", mapping.DeviceUserPin);
    }

    [Fact]
    public void Create_ConEmployeeIdVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            EmployeeDeviceMapping.Create(Guid.Empty, Guid.NewGuid(), "37"));
    }

    [Fact]
    public void Create_ConPinVacio_LanzaDomainException()
    {
        Assert.Throws<DomainException>(() =>
            EmployeeDeviceMapping.Create(Guid.NewGuid(), Guid.NewGuid(), " "));
    }

    [Fact]
    public void UpdatePin_CorrigeElPinCapturado()
    {
        var mapping = EmployeeDeviceMapping.Create(Guid.NewGuid(), Guid.NewGuid(), "1");

        mapping.UpdatePin("6");

        Assert.Equal("6", mapping.DeviceUserPin);
    }

    [Fact]
    public void ReassignEmployee_TraspasaElVinculoConservandoIdDispositivoYPin()
    {
        var deviceId = Guid.NewGuid();
        var mapping = EmployeeDeviceMapping.Create(Guid.NewGuid(), deviceId, "38");
        var originalId = mapping.Id;
        var newEmployeeId = Guid.NewGuid();

        mapping.ReassignEmployee(newEmployeeId);

        // Mismo Id: la nube recibe un upsert por Id (nunca un DELETE), así que la fila se
        // actualiza en vez de chocar con el índice único (dispositivo, PIN).
        Assert.Equal(originalId, mapping.Id);
        Assert.Equal(newEmployeeId, mapping.EmployeeId);
        Assert.Equal(deviceId, mapping.DeviceId);
        Assert.Equal("38", mapping.DeviceUserPin);
    }

    [Fact]
    public void ReassignEmployee_ConGuidVacio_LanzaDomainException()
    {
        var mapping = EmployeeDeviceMapping.Create(Guid.NewGuid(), Guid.NewGuid(), "38");

        Assert.Throws<DomainException>(() => mapping.ReassignEmployee(Guid.Empty));
    }

    [Fact]
    public void UpdatePin_ConPinVacio_LanzaDomainException()
    {
        var mapping = EmployeeDeviceMapping.Create(Guid.NewGuid(), Guid.NewGuid(), "1");

        Assert.Throws<DomainException>(() => mapping.UpdatePin(" "));
    }
}
