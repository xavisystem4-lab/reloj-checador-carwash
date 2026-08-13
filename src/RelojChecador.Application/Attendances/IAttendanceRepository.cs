using RelojChecador.Domain.Attendances;

namespace RelojChecador.Application.Attendances;

public interface IAttendanceRepository
{
    /// <summary>¿Ya existe una marcación con esta combinación exacta? Es la base de la
    /// deduplicación: la misma marcación puede llegar tanto por el monitoreo en tiempo
    /// real como por una descarga manual posterior — nunca debe guardarse dos veces.</summary>
    Task<bool> ExistsAsync(
        Guid deviceId, string deviceUserPin, DateTime timestampUtc, CancellationToken cancellationToken = default);

    Task AddAsync(Attendance attendance, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Attendance>> ListByBranchAsync(
        Guid branchId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);
}
