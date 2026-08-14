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
}
