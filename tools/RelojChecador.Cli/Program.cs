// RelojChecador.Cli — herramienta administrativa de línea de comandos.
//
// Por ahora solo tiene el comando "seed-branch-and-device", usado para registrar la
// primera sucursal y el primer dispositivo real (el ZKTeco F22/ID de pruebas) en la
// base de datos local, sin depender todavía de la pantalla de UI (Fase 3, pendiente).
// Reutiliza exactamente el mismo Domain + Infrastructure.Data que usará la app WPF —
// no es un atajo paralelo, es la misma base de código.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;
using RelojChecador.Infrastructure.Data;

var appDataDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelojChecador");
Directory.CreateDirectory(appDataDirectory);
var databasePath = Path.Combine(appDataDirectory, "relojchecador.db");

Console.WriteLine($"Base de datos local: {databasePath}");

var services = new ServiceCollection();
services.AddRelojChecadorData($"Data Source={databasePath}");
await using var provider = services.BuildServiceProvider();

await using (var scope = provider.CreateAsyncScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
    await dbContext.Database.MigrateAsync();
}

if (args.Length == 0 || args[0] != "seed-branch-and-device")
{
    Console.WriteLine("Uso: dotnet run -- seed-branch-and-device");
    return 1;
}

await using var seedScope = provider.CreateAsyncScope();
var branchRepository = seedScope.ServiceProvider.GetRequiredService<IBranchRepository>();
var deviceRepository = seedScope.ServiceProvider.GetRequiredService<IDeviceRepository>();
var unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

const string branchCode = "PRINCIPAL";
var branch = await branchRepository.GetByCodeAsync(branchCode);
if (branch is null)
{
    branch = Branch.Create(
        code: branchCode,
        name: "Sucursal Principal",
        timeZoneId: "America/Tijuana", // Mexicali, Baja California
        address: "Mexicali, Baja California, México");
    await branchRepository.AddAsync(branch);
    Console.WriteLine($"Sucursal creada: {branch.Name} ({branch.Code}) — Id {branch.Id}");
}
else
{
    Console.WriteLine($"Sucursal ya existía: {branch.Name} ({branch.Code}) — Id {branch.Id}");
}

// Datos reales del equipo confirmados por el usuario y verificados en red
// (ping + puerto TCP 4370 abiertos desde esta misma Mac, en la misma LAN 192.168.1.x).
var existingDevices = await deviceRepository.ListByBranchAsync(branch.Id);
var device = existingDevices.FirstOrDefault(d => d.SerialNumber == "CQZ7233360308");
if (device is null)
{
    device = Device.Register(
        name: "Entrada Principal",
        brand: "ZKTeco",
        model: "F22/ID",
        ipAddress: "192.168.1.66",
        tcpPort: 4370,
        branchId: branch.Id,
        timeZoneId: branch.TimeZoneId,
        serialNumber: "CQZ7233360308",
        macAddress: "00:17:61:13:19:dc");
    device.UpdateFirmwareVersion("Ver 8.0.4.3-20220708");
    await deviceRepository.AddAsync(device);
    Console.WriteLine($"Dispositivo creado: {device.Name} ({device.IpAddress}:{device.TcpPort}) — Id {device.Id}");
}
else
{
    Console.WriteLine($"Dispositivo ya existía: {device.Name} ({device.IpAddress}:{device.TcpPort}) — Id {device.Id}");
}

await unitOfWork.SaveChangesAsync();
Console.WriteLine("Listo.");
return 0;
