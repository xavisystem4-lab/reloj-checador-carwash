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

    /// <summary>Usado por el motor de sincronización con Supabase (RelojChecador.Infrastructure.Cloud):
    /// esta tabla puede crecer mucho, así que en vez de reenviar todo en cada ciclo se pide
    /// solo lo modificado después de <paramref name="sinceUtc"/> (por UpdatedAtUtc, que
    /// también avanza con ReconcileEmployee, no solo con la creación). Ordenado ascendente
    /// para poder avanzar el cursor de sincronización de forma segura y determinista.</summary>
    Task<IReadOnlyList<Attendance>> ListChangedSinceAsync(
        DateTime sinceUtc, int maxCount, CancellationToken cancellationToken = default);
}
