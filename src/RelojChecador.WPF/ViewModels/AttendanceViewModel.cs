using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Branches;
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
public sealed record AttendanceRow(Attendance Attendance, string BranchName, string DeviceName, string? EmployeeName)
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
    private const string DateFormat = "dd/MM/yyyy";

    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;

    private IReadOnlyList<AttendanceRow> _allRows = [];

    [ObservableProperty]
    private string _statusMessage = "Cargando asistencias...";

    [ObservableProperty]
    private BranchFilterOption? _selectedBranchOption;

    [ObservableProperty]
    private string _fromDateText = "";

    [ObservableProperty]
    private string _toDateText = "";

    [ObservableProperty]
    private string _searchText = "";

    public ObservableCollection<BranchFilterOption> BranchOptions { get; } = [];
    public ObservableCollection<AttendanceRow> Attendances { get; } = [];

    public AttendanceViewModel(
        IAttendanceRepository attendanceRepository, IBranchRepository branchRepository, IDeviceRepository deviceRepository,
        IEmployeeRepository employeeRepository, IEmployeeDeviceMappingRepository mappingRepository)
    {
        _attendanceRepository = attendanceRepository;
        _branchRepository = branchRepository;
        _deviceRepository = deviceRepository;
        _employeeRepository = employeeRepository;
        _mappingRepository = mappingRepository;
    }

    public async Task InitializeAsync()
    {
        var today = DateTime.Now;
        var weekAgo = today.AddDays(-7);
        FromDateText = weekAgo.ToString(DateFormat, CultureInfo.InvariantCulture);
        ToDateText = today.ToString(DateFormat, CultureInfo.InvariantCulture);

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
        if (!DateTime.TryParseExact(FromDateText.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate) ||
            !DateTime.TryParseExact(ToDateText.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate))
        {
            StatusMessage = "Las fechas deben tener el formato dd/mm/aaaa.";
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
            var employeeIdByDeviceAndPin = mappings.ToDictionary(m => (m.DeviceId, m.DeviceUserPin), m => m.EmployeeId);

            _allRows = attendances.Select(a =>
            {
                var branchName = branchNamesById.TryGetValue(a.BranchId, out var bn) ? bn : "(sucursal desconocida)";
                var deviceName = deviceNamesById.TryGetValue(a.DeviceId, out var dn) ? dn : "(dispositivo desconocido)";
                var resolvedEmployeeId = a.EmployeeId
                    ?? (employeeIdByDeviceAndPin.TryGetValue((a.DeviceId, a.DeviceUserPin), out var eid) ? eid : (Guid?)null);
                var employeeName = resolvedEmployeeId is not null && employeeNamesById.TryGetValue(resolvedEmployeeId.Value, out var en)
                    ? en
                    : null;
                return new AttendanceRow(a, branchName, deviceName, employeeName);
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

    /// <summary>Arma el CSV de lo que está mostrando el DataGrid ahora mismo (Attendances,
    /// ya con el filtro de texto aplicado) — mismas columnas y traducciones que el CSV del
    /// Dashboard web (dashboard/app.js, onExportClick), para que abra igual en Excel sin
    /// importar desde cuál de los dos se generó. Devuelve solo texto: el diálogo de
    /// "Guardar como" y la escritura a disco los maneja AttendanceView (el ViewModel no
    /// conoce tipos de WPF).</summary>
    public string BuildCsv()
    {
        var header = new[] { "Fecha y hora", "Empleado", "PIN", "Sucursal", "Dispositivo", "Método", "Tipo" };
        var lines = new List<string> { string.Join(",", header.Select(CsvEscape)) };

        foreach (var row in Attendances)
        {
            var fields = new[]
            {
                row.Attendance.TimestampUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture),
                row.EmployeeName ?? "(sin vincular)",
                row.Attendance.DeviceUserPin,
                row.BranchName,
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
