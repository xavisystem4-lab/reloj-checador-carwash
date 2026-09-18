using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Common;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Cloud;

namespace RelojChecador.WPF.Services;

/// <summary>
/// Cada pocos minutos, cierra solo los turnos que se quedaron abiertos (Entrada sin
/// Salida) ya pasada su hora de corte — pedido explícito del usuario: "si el empleado no
/// checa a su hora de salida esta se marca automáticamente para que no sigan corriendo las
/// horas". Aplica a TODO empleado activo sin horario especial: con
/// <see cref="Employee.ScheduledEndTime"/> capturado se cierra exacto a esa hora; sin él,
/// a las 8 horas desde que entró (regla general dada explícitamente por el usuario — ver
/// comentario de clase de <see cref="AttendanceAutoCloser"/>). La detección en sí es
/// lógica pura (ver esa misma clase); esta clase solo orquesta la persistencia, mismo
/// patrón Singleton+BackgroundService que <see cref="SupabaseSyncBackgroundService"/>
/// (scope propio por ciclo, un fallo puntual no tumba el host).
///
/// La Salida generada es una <see cref="Attendance"/> real, con
/// <see cref="AttendanceVerifyMethod.Automatic"/> — queda en el historial exactamente igual
/// que cualquier otra marcación (visible en la pantalla de Asistencia, exportable, y se
/// sincroniza a Supabase con el mismo motor de siempre) para que tanto el cálculo de
/// nómina de escritorio (WorkedHoursCalculator) como el reporte del Dashboard web dejen de
/// seguir sumando horas después del corte — ninguno de los dos necesita saber que este
/// corte fue automático, ambos ya saben parar de contar en cuanto existe una Salida real.
/// </summary>
public sealed class AttendanceAutoCloseBackgroundService(
    IServiceScopeFactory scopeFactory,
    SupabaseSyncBackgroundService syncService,
    ILogger<AttendanceAutoCloseBackgroundService> logger) : BackgroundService
{
    // Cada pocos minutos alcanza de sobra: el corte es contra una hora de reloj (no un
    // evento que haya que atrapar al vuelo), así que un retraso de hasta este intervalo
    // entre que se cumple la hora programada y se genera la Salida es aceptable — mismo
    // criterio de "barato, sin apuro" que el resto de los sondeos periódicos de la app.
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex,
                    "Ciclo de auto-cierre de turnos falló — se reintenta en el siguiente ciclo, nada se pierde.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        var employeeRepository = services.GetRequiredService<IEmployeeRepository>();
        var attendanceRepository = services.GetRequiredService<IAttendanceRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        // "Ahora" en la misma convención de hora de pared que TimestampUtc (ver comentario
        // de clase de AttendanceAutoCloser) — NUNCA DateTime.UtcNow, que introduciría un
        // desfase real de huso horario contra datos que no lo tienen.
        var nowUtc = DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Utc);

        var employees = await employeeRepository.ListAsync(cancellationToken);
        // HasSpecialSchedule excluido a propósito (ver comentario de esa propiedad en
        // Employee: "un Gerente cuyo horario real varía día a día y nunca se va a
        // capturar como un par fijo de horas") — mismo criterio que ya usa
        // PunctualityClassifier para no juzgar puntualidad sobre alguien cuyo horario
        // real no es el turno estándar: tampoco tiene sentido cerrarle el turno solo. Ya
        // NO se exige ScheduledEndTime — un empleado sin horario capturado también entra,
        // con la regla general de 8 horas (ver AttendanceAutoCloser.DetermineAutoCloseUtc).
        var candidates = employees
            .Where(e => e.Status == EmploymentStatus.Active && !e.HasSpecialSchedule)
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        var closedCount = 0;
        foreach (var employee in candidates)
        {
            try
            {
                // ListByEmployeeAsync solo trae marcaciones con EmployeeId YA resuelto
                // directo — a propósito NO se hace también la resolución por
                // EmployeeDeviceMapping que sí usa Payroll/AttendanceViewModel
                // (GroupByResolvedEmployee): desde v1.31.0 la app resuelve el EmployeeId al
                // guardar cada marcación nueva (ver comentario de clase de
                // AttendanceViewModel), así que cualquier turno recién abierto ya llega
                // vinculado. Una marcación vieja sin vincular nunca entra a este auto-cierre —
                // se queda igual que hoy, sin marcar salida sola.
                var attendances = await attendanceRepository.ListByEmployeeAsync(employee.Id, cancellationToken);
                var pending = AttendanceAutoCloser.FindShiftsToClose(attendances, employee.ScheduledEndTime, nowUtc);

                foreach (var toClose in pending)
                {
                    var entrada = toClose.OpenEntrada;

                    // Colisión extremadamente rara (ya existe una marcación real justo en ese
                    // segundo, mismo Device/Pin) — se deja para el siguiente ciclo en vez de
                    // arriesgar el índice único (DeviceId, DeviceUserPin, TimestampUtc).
                    if (await attendanceRepository.ExistsAsync(
                            entrada.DeviceId, entrada.DeviceUserPin, toClose.CutoffUtc, cancellationToken))
                    {
                        continue;
                    }

                    var motivo = employee.ScheduledEndTime is { } scheduledEnd
                        ? $"horario {scheduledEnd:HH\\:mm}"
                        : $"sin horario capturado, regla general de {AttendanceAutoCloser.DefaultShiftDuration.TotalHours:0} horas";
                    var rawPayload =
                        $"AUTOCLOSE|{employee.FullName}|cerrado automáticamente a las {toClose.CutoffUtc:yyyy-MM-dd HH:mm:ss} " +
                        $"({motivo}, entrada sin cerrar desde {entrada.TimestampUtc:yyyy-MM-dd HH:mm:ss})";

                    // Mismo Device/Pin/BranchId que la Entrada que cierra — así la nueva Salida
                    // empareja con ella (ver WorkedHoursCalculator.PairAndSum), no con un
                    // dispositivo arbitrario.
                    var salida = Attendance.Create(
                        entrada.DeviceId, entrada.BranchId, entrada.DeviceUserPin, toClose.CutoffUtc,
                        AttendanceVerifyMethod.Automatic, ShiftPunchTypeClassifier.SalidaCode, rawPayload, employee.Id);

                    await attendanceRepository.AddAsync(salida, cancellationToken);
                    closedCount++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // AISLADO por empleado — mismo criterio que
                // SupabaseSyncBackgroundService.PushFullTableAsync ("cada tabla se sube de
                // forma aislada"): antes, un dato problemático en UN empleado (p. ej. una
                // marcación con datos inconsistentes) tumbaba la excepción hasta el catch
                // general de ExecuteAsync ANTES de llegar al SaveChangesAsync de más abajo —
                // perdiendo el cierre de TODOS los demás empleados ya procesados en ese
                // ciclo, y repitiéndose cada 5 minutos para siempre porque nada en los datos
                // cambia solo. Caso real detectado el 2026-09-18: cero cierres automáticos
                // desde el 2026-09-11 pese a que las marcaciones normales del reloj seguían
                // llegando sin problema — 18 empleados con turnos abiertos hasta 12 días.
                logger.LogWarning(ex,
                    "Auto-cierre: falló al procesar al empleado {EmployeeId} ({EmployeeName}) — se omite y se " +
                    "sigue con los demás; se reintenta este empleado en el siguiente ciclo.",
                    employee.Id, employee.FullName);
            }
        }

        if (closedCount > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Auto-cierre de turnos: se generaron {Count} marcación(es) de Salida automática.", closedCount);

            // Sube de inmediato al Dashboard — mismo criterio que cualquier otra marcación
            // nueva (ver DevicesViewModel.PersistAndTriggerSyncAsync), en vez de esperar al
            // próximo ciclo automático de SupabaseSyncBackgroundService.
            await syncService.TriggerSyncNowAsync(cancellationToken);
        }
    }
}
