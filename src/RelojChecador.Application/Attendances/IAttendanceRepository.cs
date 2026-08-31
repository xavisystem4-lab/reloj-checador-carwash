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

    /// <summary>Usado para editar o borrar UNA marcación puntual — pedido explícito del
    /// usuario: "que las asistencias se puedan editar ... o eliminar Marcación" (ver
    /// AttendanceViewModel.EditAttendanceAsync/DeleteAttendanceAsync).</summary>
    Task<Attendance?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Borrado físico LOCAL de UNA marcación — pedido explícito del usuario ("o
    /// eliminar Marcación"), reemplaza la decisión de diseño anterior de esta clase
    /// (registro de auditoría inmutable, nunca se borra). Este método solo toca la base
    /// local — quien llama (AttendanceViewModel.DeleteAttendanceAsync/BulkDeleteAsync) es
    /// responsable de además propagar el borrado a Supabase por su cuenta (ver
    /// SupabaseSyncBackgroundService.TryDeleteAttendancesRemoteAsync — pedido explícito del
    /// usuario: "podemos borrar en el sistema y que también mande la señal al sitio web"),
    /// ya que el resto del motor de sincronización solo empuja cambios y nunca borra.</summary>
    Task RemoveAsync(Attendance attendance, CancellationToken cancellationToken = default);

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

    /// <summary>Todas las marcaciones de un empleado, sin límite de fecha — usado SOLO para
    /// desvincularlas (Attendance.ReconcileEmployee(null, null)) antes de un borrado físico
    /// del empleado (ver IEmployeeRepository.RemoveAsync): la marcación en sí NUNCA se
    /// borra (es un registro de auditoría, ver comentario de clase de Attendance), solo se
    /// queda "sin vincular" otra vez, igual que antes de que existiera el vínculo.</summary>
    Task<IReadOnlyList<Attendance>> ListByEmployeeAsync(Guid employeeId, CancellationToken cancellationToken = default);

    /// <summary>Marcaciones sin vincular a ningún empleado (EmployeeId null), de CUALQUIER
    /// dispositivo/PIN y SIN límite de fecha (a diferencia de ListUnresolvedByDeviceAndPinAsync,
    /// que solo busca un PIN puntual) — usado por "Vincular pendientes" en Empleados para
    /// mostrar de un vistazo todo lo que falta por vincular en el sistema, sin importar qué
    /// tan vieja sea la marcación. <paramref name="maxCount"/> es el mismo tope defensivo que
    /// el resto de las consultas de esta tabla.</summary>
    Task<IReadOnlyList<Attendance>> ListUnresolvedAsync(int maxCount, CancellationToken cancellationToken = default);

    /// <summary>Marcaciones de UN empleado dentro de un rango [fromUtc, toUtc) — usado por
    /// ShiftPunchTypeClassifier (vía DevicesViewModel.PersistAttendanceAsync) para saber si
    /// ya tiene un turno abierto hoy antes de clasificar una marcación nueva. El rango lo
    /// arma quien llama (normalmente medianoche a medianoche del día de la marcación nueva,
    /// "hoy" en la misma hora de pared que usa el resto de la app, sin conversión de zona
    /// horaria real — ver comentario de Device.SyncDeviceTimeAsync).</summary>
    Task<IReadOnlyList<Attendance>> ListByEmployeeInRangeAsync(
        Guid employeeId, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default);

    /// <summary>Cambia el PunchType de TODAS las marcaciones existentes de un solo jalón —
    /// usado por el botón "Normalizar tipos de marcación" (pedido explícito del usuario:
    /// "ahorita todas las checadas marcalas como entradas normales incluso las que ya
    /// checaron"), una corrección de datos de una sola vez antes de que
    /// ShiftPunchTypeClassifier empiece a clasificar las marcaciones NUEVAS que lleguen de
    /// aquí en adelante.</summary>
    Task<int> SetAllPunchTypesAsync(int punchType, CancellationToken cancellationToken = default);
}
