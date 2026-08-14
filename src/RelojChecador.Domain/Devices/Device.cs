using RelojChecador.Domain.Common;

namespace RelojChecador.Domain.Devices;

/// <summary>
/// Un reloj checador biométrico registrado en una sucursal. "Brand"/"Model" son texto
/// libre (no un enum) a propósito: la arquitectura de adaptadores (Fase 2) debe poder
/// incorporar marcas futuras sin tocar el Domain.
/// </summary>
public sealed class Device : AuditableEntity
{
    public string Name { get; private set; } = null!;
    public string Brand { get; private set; } = null!;
    public string Model { get; private set; } = null!;
    public string? SerialNumber { get; private set; }
    public string? MacAddress { get; private set; }
    public string IpAddress { get; private set; } = null!;
    public int TcpPort { get; private set; }
    public string? MachineNumber { get; private set; }
    public Guid BranchId { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public DeviceStatus Status { get; private set; } = DeviceStatus.Unknown;
    public DateTime? LastCommunicationAtUtc { get; private set; }
    public DateTime? LastSyncAtUtc { get; private set; }
    public string? FirmwareVersion { get; private set; }
    public DeviceCapabilities Capabilities { get; private set; } = DeviceCapabilities.None;

    /// <summary>Clave hacia la entrada correspondiente en Windows Credential Manager.
    /// Nunca almacena el secreto/credencial en sí — ver RelojChecador.Infrastructure.Security.</summary>
    public string? CredentialReference { get; private set; }

    private Device()
    {
        // Constructor privado para EF Core.
    }

    public static Device Register(
        string name,
        string brand,
        string model,
        string ipAddress,
        int tcpPort,
        Guid branchId,
        string timeZoneId,
        string? serialNumber = null,
        string? macAddress = null,
        string? machineNumber = null)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(brand, nameof(brand));
        Guard.AgainstNullOrWhiteSpace(model, nameof(model));
        Guard.AgainstNullOrWhiteSpace(ipAddress, nameof(ipAddress));
        Guard.AgainstNullOrWhiteSpace(timeZoneId, nameof(timeZoneId));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        Guard.AgainstOutOfRange(tcpPort, 1, 65535, nameof(tcpPort));

        var device = new Device
        {
            Id = Guid.CreateVersion7(),
            Name = name.Trim(),
            Brand = brand.Trim(),
            Model = model.Trim(),
            IpAddress = ipAddress.Trim(),
            TcpPort = tcpPort,
            BranchId = branchId,
            TimeZoneId = timeZoneId.Trim(),
            SerialNumber = serialNumber?.Trim(),
            MacAddress = macAddress?.Trim(),
            MachineNumber = machineNumber?.Trim(),
            Status = DeviceStatus.Unknown,
            Capabilities = DeviceCapabilities.None,
        };
        device.InitializeAuditFields();
        return device;
    }

    /// <summary>Corrige los datos capturados al dar de alta el dispositivo (nombre, marca,
    /// modelo, sucursal, zona horaria, número de serie, MAC) — nunca su identidad de red
    /// (IP/puerto, ver UpdateNetworkSettings, que se llama aparte) ni su estado de
    /// comunicación. Permite reasignar BranchId (mover el reloj de sucursal), a diferencia
    /// de Employee.Number/Branch.Code que son claves de negocio: el Id del dispositivo, no
    /// su sucursal, es lo que lo identifica en el resto del sistema.</summary>
    public void UpdateDetails(
        string name, string brand, string model, Guid branchId, string timeZoneId,
        string? serialNumber, string? macAddress)
    {
        Guard.AgainstNullOrWhiteSpace(name, nameof(name));
        Guard.AgainstNullOrWhiteSpace(brand, nameof(brand));
        Guard.AgainstNullOrWhiteSpace(model, nameof(model));
        Guard.AgainstEmptyGuid(branchId, nameof(branchId));
        Guard.AgainstNullOrWhiteSpace(timeZoneId, nameof(timeZoneId));

        Name = name.Trim();
        Brand = brand.Trim();
        Model = model.Trim();
        BranchId = branchId;
        TimeZoneId = timeZoneId.Trim();
        SerialNumber = serialNumber?.Trim();
        MacAddress = macAddress?.Trim();
        Touch();
    }

    public void UpdateNetworkSettings(string ipAddress, int tcpPort)
    {
        Guard.AgainstNullOrWhiteSpace(ipAddress, nameof(ipAddress));
        Guard.AgainstOutOfRange(tcpPort, 1, 65535, nameof(tcpPort));
        IpAddress = ipAddress.Trim();
        TcpPort = tcpPort;
        Touch();
    }

    public void UpdateCapabilities(DeviceCapabilities capabilities)
    {
        Capabilities = capabilities;
        Touch();
    }

    public void UpdateFirmwareVersion(string? firmwareVersion)
    {
        FirmwareVersion = firmwareVersion?.Trim();
        Touch();
    }

    public void AssignCredentialReference(string reference)
    {
        Guard.AgainstNullOrWhiteSpace(reference, nameof(reference));
        CredentialReference = reference.Trim();
        Touch();
    }

    /// <summary>Registra una comunicación exitosa (no solo un ping — comunicación completa).</summary>
    public void RecordSuccessfulCommunication(DateTime atUtc)
    {
        LastCommunicationAtUtc = atUtc;
        Status = DeviceStatus.Online;
        Touch();
    }

    public void RecordFailedCommunication()
    {
        Status = DeviceStatus.Offline;
        Touch();
    }

    public void RecordSync(DateTime atUtc)
    {
        LastSyncAtUtc = atUtc;
        Touch();
    }

    public void Disable()
    {
        Status = DeviceStatus.Disabled;
        Touch();
    }
}
