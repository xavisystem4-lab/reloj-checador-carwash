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

    /// <summary>Igual que <see cref="ListByBranchAsync"/> pero de todas las sucursales —
    /// usado por la pantalla de Asistencia cuando el filtro de sucursal está en "Todas".
    /// <paramref name="maxCount"/> es un tope defensivo (esta tabla puede crecer mucho con
    /// el tiempo, ver comentario de ListChangedSinceAsync) para no cargar un histórico
    /// completo sin querer solo porque el usuario dejó un rango de fechas muy amplio.</summary>
    Task<IReadOnlyList<Attendance>> ListAsync(
        DateTime fromUtc, DateTime toUtc, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>Marcaciones de un dispositivo+PIN que todavía no están conciliadas con
    /// ningún Employee (EmployeeId null) — usado para la conciliación retroactiva al crear
    /// un EmployeeDeviceMapping tardío (ver EmployeesViewModel.CreateMappingAsync y
    /// Attendance.ReconcileEmployee).</summary>
    Task<IReadOnlyList<Attendance>> ListUnresolvedByDeviceAndPinAsync(
        Guid deviceId, string deviceUserPin, CancellationToken cancellationToken = default);

    /// <summary>Usado por el motor de sincronización con Supabase (RelojChecador.Infrastructure.Cloud):
    /// esta tabla puede crecer mucho, así que en vez de reenviar todo en cada ciclo se pide
    /// solo lo modificado después de <paramref name="sinceUtc"/> (por UpdatedAtUtc, que
    /// también avanza con ReconcileEmployee, no solo con la creación). Ordenado ascendente
    /// para poder avanzar el cursor de sincronización de forma segura y determinista.</summary>
    Task<IReadOnlyList<Attendance>> ListChangedSinceAsync(
        DateTime sinceUtc, int maxCount, CancellationToken cancellationToken = default);
}
