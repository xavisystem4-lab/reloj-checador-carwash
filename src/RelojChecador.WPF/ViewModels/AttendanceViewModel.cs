using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;
using RelojChecador.Infrastructure.Cloud;
using RelojChecador.WPF.Converters;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>Opción del combo de sucursal — incluye "Todas las sucursales" (Branch null),
/// que no existe como valor real en el dominio.</summary>
public sealed record BranchFilterOption(Branch? Branch, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Une una <see cref="Attendance"/> con sucursal/dispositivo/empleado ya
/// resueltos — mismos tres cruces que EmployeesViewModel.EmployeeRow ya hace para
/// Branch/Device, más la resolución de empleado (ver comentario de clase del ViewModel).</summary>
public sealed record AttendanceRow(Attendance Attendance, string BranchName, string DeviceName, string? EmployeeName, string? Department)
{
    public string EmployeeDisplay => EmployeeName ?? $"PIN {Attendance.DeviceUserPin} · sin vincular";
}

/// <summary>
/// ViewModel de la pantalla de Asistencia: consulta las marcaciones ya guardadas
/// localmente (Attendance), con nombre de empleado resuelto en vez del PIN crudo — mismo
/// criterio de resolución que ya usa el Dashboard web (dashboard/app.js,
/// enrichAttendances): primero Attendance.EmployeeId directo (lo llena la conciliación
/// retroactiva de EmployeesViewModel.CreateMappingAsync), si no hay, se busca por
/// EmployeeDeviceMapping (DeviceId+DeviceUserPin).
///
/// Filtros: sucursal (opcional, "Todas" por defecto), rango de fechas (últimos 7 días por
/// defecto, mismo criterio que el Dashboard web) y texto libre (nombre o PIN). Los tres
/// primeros requieren presionar "Actualizar" (van a la base); el de texto se aplica en
/// memoria sobre lo ya cargado, así que filtra al escribir sin recargar nada.
///
/// Attendance es de solo lectura: es un registro de auditoría, nunca se edita ni se borra
/// desde la UI (ver comentario de la clase en el dominio) — por eso esta pantalla no tiene
/// ningún botón de alta/edición, a diferencia de Sucursales/Empleados/Dispositivos.
/// </summary>
public sealed partial class AttendanceViewModel : ObservableObject
{
    private const int MaxRows = 2000;

    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly SupabaseSyncBackgroundService _syncService;

    private IReadOnlyList<AttendanceRow> _allRows = [];

    [ObservableProperty]
    private string _statusMessage = "Cargando asistencias...";

    [ObservableProperty]
    private BranchFilterOption? _selectedBranchOption;

    // DateTime? en vez de texto — pedido explícito del usuario: "que tenga función de
    // calendario y escribirlo manual también". El DatePicker de WPF (ver AttendanceView.xaml)
    // ya da las dos cosas con un solo control: el calendario desplegable Y un cuadro de
    // texto editable a mano, sin tener que armar ninguno de los dos por separado.
    [ObservableProperty]
    private DateTime? _fromDateText;

    [ObservableProperty]
    private DateTime? _toDateText;

    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<BranchFilterOption> BranchOptions { get; } = [];
    public ObservableCollection<AttendanceRow> Attendances { get; } = [];

    public AttendanceViewModel(
        IAttendanceRepository attendanceRepository, IBranchRepository branchRepository, IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository, IEmployeeDeviceMappingRepository mappingRepository,
        IUnitOfWork unitOfWork, SupabaseSyncBackgroundService syncService)
    {
        _attendanceRepository = attendanceRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _mappingRepository = mappingRepository;
        _unitOfWork = unitOfWork;
        _syncService = syncService;
    }

    public async Task InitializeAsync()
    {
        var today = DateTime.Now.Date;
        var weekAgo = today.AddDays(-7);
        FromDateText = weekAgo;
        ToDateText = today;

        var branches = await _branchRepository.ListAsync();
        BranchOptions.Clear();
        BranchOptions.Add(new BranchFilterOption(null, "Todas las sucursales"));
        foreach (var branch in branches.OrderBy(b => b.Name))
        {
            BranchOptions.Add(new BranchFilterOption(branch, branch.Name));
        }
        SelectedBranchOption = BranchOptions[0];

        await LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (FromDateText is not { } fromDate || ToDateText is not { } toDate)
        {
            StatusMessage = "Selecciona un rango de fechas completo (Desde y Hasta).";
            return;
        }

        StatusMessage = "Cargando asistencias...";
        try
        {
            // Igual criterio que el resto de la app (ver Device.SyncDeviceTimeAsync): no
            // hay conversión real de zona horaria, se asume que todo el negocio opera en
            // una sola — "Utc" aquí es solo la marca que exige TimestampUtc, no una
            // conversión de la hora local de Mexicali a UTC real.
            var fromUtc = DateTime.SpecifyKind(fromDate, DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(toDate.AddDays(1).AddTicks(-1), DateTimeKind.Utc);

            var branchFilter = SelectedBranchOption?.Branch;
            var attendances = branchFilter is not null
                ? await _attendanceRepository.ListByBranchAsync(branchFilter.Id, fromUtc, toUtc)
                : await _attendanceRepository.ListAsync(fromUtc, toUtc, MaxRows);

            var branches = await _branchRepository.ListAsync();
            var devices = await _deviceRepository.ListAsync();
            var employees = await _employeeRepository.ListAsync();
            var mappings = await _mappingRepository.ListAsync();

            var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);
            var deviceNamesById = devices.ToDictionary(d => d.Id, d => d.Name);
            var employeeNamesById = employees.ToDictionary(e => e.Id, e => e.FullName);
            // Pedido explícito del usuario tras fusionar varias sucursales en una sola:
            // "que en los reportes se acomoden por sucursal" — Department conserva la
            // ubicación original de quien se fusionó (ver EmployeesViewModel.ApplyCatalogReplaceAsync),
            // así se puede seguir distinguiendo de dónde era cada quien.
            var employeeDepartmentsById = employees.ToDictionary(e => e.Id, e => e.Department);
            var employeeIdByDeviceAndPin = mappings.ToDictionary(m => (m.DeviceId, m.DeviceUserPin), m => m.EmployeeId);

            _allRows = attendances.Select(a =>
            {
                // BranchId nulo = "pendiente de asignación" (ver comentario de clase de
                // Attendance): el PIN todavía no está vinculado a ningún empleado, así que
                // tampoco se conoce su sucursal — se muestra así en vez de "(sucursal
                // desconocida)" para que el admin sepa que la corrección es vincular el PIN
                // en Empleados (ver EmployeesViewModel.ReconcileAttendancesAsync), no un
                // dato roto.
                var branchName = a.BranchId is { } branchId
                    ? (branchNamesById.TryGetValue(branchId, out var bn) ? bn : "(sucursal desconocida)")
                    : "Pendiente de asignación";
                var deviceName = deviceNamesById.TryGetValue(a.DeviceId, out var dn) ? dn : "(dispositivo desconocido)";
                var resolvedEmployeeId = a.EmployeeId
                    ?? (employeeIdByDeviceAndPin.TryGetValue((a.DeviceId, a.DeviceUserPin), out var eid) ? eid : (Guid?)null);
                var employeeName = resolvedEmployeeId is not null && employeeNamesById.TryGetValue(resolvedEmployeeId.Value, out var en)
                    ? en
                    : null;
                var department = resolvedEmployeeId is not null && employeeDepartmentsById.TryGetValue(resolvedEmployeeId.Value, out var dep)
                    ? dep
                    : null;
                return new AttendanceRow(a, branchName, deviceName, employeeName, department);
            }).ToList();

            ApplySearchFilter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de asistencias.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
    }

    partial void OnSearchTextChanged(string value) => ApplySearchFilter();

    /// <summary>Filtra _allRows (ya cargado desde la base) por SearchText, sin volver a
    /// consultar nada — mismo patrón que el buscador del Dashboard web.</summary>
    private void ApplySearchFilter()
    {
        var term = SearchText.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? _allRows
            : _allRows.Where(r =>
                (r.EmployeeName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
                r.Attendance.DeviceUserPin.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

        Attendances.Clear();
        foreach (var row in filtered)
        {
            Attendances.Add(row);
        }

        if (filtered.Count == 0)
        {
            StatusMessage = "Sin marcaciones para estos filtros.";
            return;
        }

        var unresolvedCount = filtered.Count(r => r.EmployeeName is null);
        StatusMessage = unresolvedCount > 0
            ? $"{filtered.Count} marcación(es) encontrada(s) ({unresolvedCount} sin vincular a empleado)."
            : $"{filtered.Count} marcación(es) encontrada(s).";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // "Marcar asistencia manual" — pedido explícito del usuario: poder registrar a alguien
    // que se le olvidó checar, sin depender del reloj físico, y que aparezca sincronizada
    // en el Dashboard/nube igual que cualquier otra (aclarado explícitamente con el
    // usuario: NO significa escribirla en la memoria del dispositivo — eso no es
    // técnicamente posible, el reloj solo genera marcaciones de huellas/tarjeta reales que
    // él mismo detecta). Sigue siendo create-only (ver comentario de clase de Attendance):
    // esto agrega una fila nueva, nunca edita ni borra una existente.
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Empleados activos, para el selector del diálogo — ordenados por nombre.</summary>
    public async Task<IReadOnlyList<Employee>> GetActiveEmployeesForManualEntryAsync()
    {
        var employees = await _employeeRepository.ListAsync();
        return employees.Where(e => e.Status == EmploymentStatus.Active).OrderBy(e => e.FullName).ToList();
    }

    public sealed record ManualAttendanceOutcome(string? Error)
    {
        public bool Success => Error is null;
    }

    /// <summary>Crea una marcación manual. <see cref="Attendance.DeviceId"/> es un campo
    /// obligatorio del dominio (nunca hizo falta que fuera opcional hasta ahora) — en vez
    /// de agregar una migración de esquema para permitirlo null, se resuelve solo: el
    /// dispositivo/PIN ya vinculado al empleado si existe (<see cref="EmployeeDeviceMapping"/>),
    /// o si no, el ÚNICO dispositivo que exista en todo el sistema (con un PIN placeholder
    /// "MANUAL", ya que nunca se enroló ahí de verdad) — pedido explícito del usuario: un
    /// solo reloj físico compartido atiende a empleados de cualquier sucursal, así que ya
    /// NO se filtra por Device.BranchId == employee.BranchId (ese filtro asumía "un reloj
    /// por sucursal", justo lo contrario del escenario real). Si hay 0 o 2+ dispositivos en
    /// todo el sistema y el empleado no tiene ningún vínculo propio, no hay forma no
    /// ambigua de resolverlo — se reporta el error en vez de adivinar cuál usar. La
    /// sucursal de la marcación (<see cref="Attendance.BranchId"/>) sigue siendo siempre
    /// <c>employee.BranchId</c>, sin importar cuál dispositivo se haya resuelto aquí.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó
    /// correctamente.</returns>
    public async Task<ManualAttendanceOutcome> CreateManualAttendanceAsync(
        Guid employeeId, DateTime timestampLocal, int punchType)
    {
        try
        {
            var employee = await _employeeRepository.GetByIdAsync(employeeId);
            if (employee is null)
            {
                return new ManualAttendanceOutcome(
                    "No se encontró el empleado — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.");
            }

            if (timestampLocal > DateTime.Now.AddMinutes(5))
            {
                return new ManualAttendanceOutcome("La fecha y hora no puede ser en el futuro.");
            }

            var mappings = await _mappingRepository.ListAsync();
            var employeeMapping = mappings.FirstOrDefault(m => m.EmployeeId == employeeId);

            Guid deviceId;
            string deviceUserPin;

            if (employeeMapping is not null)
            {
                deviceId = employeeMapping.DeviceId;
                deviceUserPin = employeeMapping.DeviceUserPin;
            }
            else
            {
                var allDevices = await _deviceRepository.ListAsync();

                if (allDevices.Count != 1)
                {
                    return new ManualAttendanceOutcome(allDevices.Count == 0
                        ? $"\"{employee.FullName}\" no está vinculado a ningún reloj y no hay ningún dispositivo registrado — registra uno primero en Dispositivos."
                        : $"\"{employee.FullName}\" no está vinculado a ningún reloj y hay varios dispositivos registrados — vincúlalo primero a uno específico desde Empleados.");
                }

                deviceId = allDevices[0].Id;
                deviceUserPin = "MANUAL";
            }

            // TimestampUtc en este proyecto NUNCA es UTC real (ver el comentario de
            // LoadAsync más arriba) — es la hora local de pared del negocio, solo
            // etiquetada como UTC. Una captura manual sigue la misma convención:
            // DateTimeKind.Utc aquí es solo la marca que exige el campo, no una
            // conversión real de huso horario.
            var timestampUtc = DateTime.SpecifyKind(timestampLocal, DateTimeKind.Utc);

            if (await _attendanceRepository.ExistsAsync(deviceId, deviceUserPin, timestampUtc))
            {
                return new ManualAttendanceOutcome("Ya existe una marcación idéntica (mismo dispositivo/PIN/hora) — no se creó otra.");
            }

            var rawPayload = $"MANUAL|{employee.FullName}|capturado {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            var attendance = Attendance.Create(
                deviceId, employee.BranchId, deviceUserPin, timestampUtc,
                AttendanceVerifyMethod.Manual, punchType, rawPayload, employeeId);

            await _attendanceRepository.AddAsync(attendance);
            await _unitOfWork.SaveChangesAsync();

            // Igual criterio que cada marcación nueva del dispositivo real (ver
            // DevicesViewModel.PersistAndTriggerSyncAsync): no espera al ciclo automático
            // de Supabase (hasta IntervalSeconds), la sube de inmediato para que aparezca
            // en el Dashboard sin demora.
            await _syncService.TriggerSyncNowAsync();

            await LoadAsync();
            return new ManualAttendanceOutcome(null);
        }
        catch (DomainException ex)
        {
            return new ManualAttendanceOutcome(ex.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear una asistencia manual (EmployeeId={EmployeeId})", employeeId);
            return new ManualAttendanceOutcome("Ocurrió un error inesperado al guardar. Revisa el registro de errores.");
        }
    }

    /// <summary>Arma el CSV de lo que está mostrando el DataGrid ahora mismo (Attendances,
    /// ya con el filtro de texto aplicado) — mismas columnas y traducciones que el CSV del
    /// Dashboard web (dashboard/app.js, onExportClick), para que abra igual en Excel sin
    /// importar desde cuál de los dos se generó. Devuelve solo texto: el diálogo de
    /// "Guardar como" y la escritura a disco los maneja AttendanceView (el ViewModel no
    /// conoce tipos de WPF).</summary>
    public string BuildCsv()
    {
        var header = new[] { "Fecha y hora", "Empleado", "PIN", "Sucursal", "Departamento", "Dispositivo", "Método", "Tipo" };
        var lines = new List<string> { string.Join(",", header.Select(CsvEscape)) };

        foreach (var row in Attendances)
        {
            var fields = new[]
            {
                row.Attendance.TimestampUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                row.EmployeeName ?? "(sin vincular)",
                row.Attendance.DeviceUserPin,
                row.BranchName,
                row.Department ?? "",
                row.DeviceName,
                VerifyMethodToTextConverter.Describe(row.Attendance.VerifyMethod),
                PunchTypeToTextConverter.Describe(row.Attendance.PunchType),
            };
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        return string.Join("\r\n", lines);
    }

    private static string CsvEscape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
