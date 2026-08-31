using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Attendances;
using RelojChecador.Domain.Attendances;

namespace RelojChecador.Infrastructure.Data.Repositories;

public sealed class EfAttendanceRepository(RelojChecadorDbContext dbContext) : IAttendanceRepository
{
    public Task<bool> ExistsAsync(
        Guid deviceId, string deviceUserPin, DateTime timestampUtc, CancellationToken cancellationToken = default) =>
        dbContext.Attendances.AnyAsync(
            a => a.DeviceId == deviceId && a.DeviceUserPin == deviceUserPin && a.TimestampUtc == timestampUtc,
            cancellationToken);

    public async Task AddAsync(Attendance attendance, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances.AddAsync(attendance, cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListByBranchAsync(
        Guid branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.BranchId == branchId && a.TimestampUtc >= fromUtc && a.TimestampUtc <= toUtc)
            .OrderByDescending(a => a.TimestampUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.TimestampUtc >= fromUtc && a.TimestampUtc <= toUtc)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListUnresolvedByDeviceAndPinAsync(
        Guid deviceId, string deviceUserPin, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.DeviceId == deviceId && a.DeviceUserPin == deviceUserPin && a.EmployeeId == null)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListChangedSinceAsync(
        DateTime sinceUtc, int maxCount, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.UpdatedAtUtc > sinceUtc)
            .OrderBy(a => a.UpdatedAtUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListByEmployeeAsync(
        Guid employeeId, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances.Where(a => a.EmployeeId == employeeId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListUnresolvedAsync(int maxCount, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.EmployeeId == null)
            .OrderByDescending(a => a.TimestampUtc)
            .Take(maxCount)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Attendance>> ListByEmployeeInRangeAsync(
        Guid employeeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default) =>
        await dbContext.Attendances
            .Where(a => a.EmployeeId == employeeId && a.TimestampUtc >= fromUtc && a.TimestampUtc < toUtc)
            .OrderBy(a => a.TimestampUtc)
            .ToListAsync(cancellationToken);

    public async Task<int> SetAllPunchTypesAsync(int punchType, CancellationToken cancellationToken = default)
    {
        // ExecuteUpdateAsync es una sola sentencia SQL (rápido incluso con miles de filas),
        // pero se salta por completo a Attendance.Touch() — hay que replicar a mano el
        // efecto que de verdad importa aquí (UpdatedAtUtc) o el motor de sincronización
        // nunca se entera de este cambio (ver ListChangedSinceAsync, que filtra por
        // UpdatedAtUtc) y el Dashboard web se queda mostrando el PunchType viejo para
        // siempre. ConcurrencyToken se deja tal cual a propósito: Guid.NewGuid() por fila no
        // es traducible a SQL en una sola sentencia (a diferencia de un valor constante como
        // UpdatedAtUtc), y no regenerarlo aquí no tiene ningún efecto práctico — nadie más
        // debería estar editando estas filas al mismo tiempo que corre esta corrección.
        var now = DateTime.UtcNow;
        return await dbContext.Attendances.ExecuteUpdateAsync(
            setters => setters
                .SetProperty(a => a.PunchType, punchType)
                .SetProperty(a => a.UpdatedAtUtc, now),
            cancellationToken);
    }
}
