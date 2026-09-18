// RelojChecador.Cli — herramienta administrativa de línea de comandos.
//
// Comandos:
// - "seed-branch-and-device": registra la primera sucursal y el primer dispositivo real
//   (el ZKTeco F22/ID de pruebas) en la base de datos local, sin depender todavía de la
//   pantalla de UI (Fase 3, pendiente). Reutiliza exactamente el mismo Domain +
//   Infrastructure.Data que usará la app WPF — no es un atajo paralelo, es la misma base
//   de código.
// - "fix-branch-id": repara una sucursal local cuyo Id NO coincide con el de Supabase
//   para el mismo Code (ver comentario junto a RunFixBranchIdAsync más abajo para el caso
//   real que motivó esto).

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

if (args.Length > 0 && args[0] == "fix-branch-id")
{
    if (args.Length != 3 || !Guid.TryParse(args[2], out var remoteId))
    {
        Console.WriteLine("Uso: dotnet run -- fix-branch-id <code> <id-correcto-en-supabase>");
        return 1;
    }

    await using var fixScope = provider.CreateAsyncScope();
    return await RunFixBranchIdAsync(
        fixScope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>(), args[1], remoteId);
}

if (args.Length == 0 || args[0] != "seed-branch-and-device")
{
    Console.WriteLine("Uso: dotnet run -- seed-branch-and-device");
    Console.WriteLine("     dotnet run -- fix-branch-id <code> <id-correcto-en-supabase>");
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

// Caso real que motivó esto (2026-09-18, sucursal "CAFETERIA"): esta instalación no tenía
// localmente ninguna sucursal con ese código (se había borrado de aquí en algún momento
// — ver MainViewModel.HardDeleteBranchesAsync, "🗑️ Borrar" en Sucursales — sin que ese
// borrado físico se avisara nunca a Supabase, porque el motor de sincronización es
// push-only y nunca envía DELETE). "Reemplazar catálogo maestro" no encontró la sucursal
// localmente y creó una nueva con Id propio; al sincronizar, Supabase la rechazó (409) por
// el índice único de Code, que YA tenía una fila con ese mismo código bajo OTRO Id — y esa
// misma sucursal recién creada se llevó entre las patas a todos los empleados que se le
// asignaron (fallan por FK contra una sucursal que nunca llegó a existir del lado de la
// nube). Esta reparación NO habla con Supabase: corrige el Id de la sucursal local (y de
// quien la referencia) para que coincida con el Id real ya existente en la nube — la
// próxima sincronización entonces actualiza esa misma fila remota en vez de chocar con
// ella, y los empleados que dependían de este Id sincronizan sin más intervención.
static async Task<int> RunFixBranchIdAsync(RelojChecadorDbContext dbContext, string code, Guid remoteId)
{
    var normalizedCode = code.Trim().ToUpperInvariant();
    var branch = await dbContext.Branches.SingleOrDefaultAsync(b => b.Code == normalizedCode);
    if (branch is null)
    {
        Console.WriteLine($"No hay ninguna sucursal local con código '{normalizedCode}'. Nada que reparar.");
        return 1;
    }

    if (branch.Id == remoteId)
    {
        Console.WriteLine($"La sucursal '{normalizedCode}' ya tiene el Id correcto ({remoteId}). Nada que reparar.");
        return 0;
    }

    if (await dbContext.Branches.AnyAsync(b => b.Id == remoteId))
    {
        Console.WriteLine(
            $"Ya existe OTRA sucursal local con Id {remoteId} — no se puede reparar automáticamente, " +
            "revisa manualmente antes de continuar (podría fusionarse con la existente).");
        return 1;
    }

    var oldId = branch.Id;
    Console.WriteLine($"Sucursal '{normalizedCode}': Id local {oldId} -> Id correcto {remoteId}.");

    await using var transaction = await dbContext.Database.BeginTransactionAsync();

    var employeesUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE Employees SET BranchId = {remoteId} WHERE BranchId = {oldId}");
    var devicesUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE Devices SET BranchId = {remoteId} WHERE BranchId = {oldId}");
    var attendancesUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE Attendances SET BranchId = {remoteId} WHERE BranchId = {oldId}");
    var branchesUpdated = await dbContext.Database.ExecuteSqlInterpolatedAsync(
        $"UPDATE Branches SET Id = {remoteId} WHERE Id = {oldId}");

    await transaction.CommitAsync();

    Console.WriteLine(
        $"Listo. Sucursal actualizada: {branchesUpdated}. Empleados reasignados: {employeesUpdated}. " +
        $"Dispositivos reasignados: {devicesUpdated}. Marcaciones reasignadas: {attendancesUpdated}.");
    Console.WriteLine("Abre la app y espera al próximo ciclo de sincronización (o usa \"Conectar con nube\") para confirmarlo.");
    return 0;
}
