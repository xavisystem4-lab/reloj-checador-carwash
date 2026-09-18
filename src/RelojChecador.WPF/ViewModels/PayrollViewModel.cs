using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Payroll;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.Payroll;
using RelojChecador.Infrastructure.Cloud;
using RelojChecador.Infrastructure.Cloud.Dtos;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>Montos de deducción de un empleado en la semana actual — forma de UI, no de
/// dominio (mismo criterio que <c>EmployeeMappingInfo</c> en EmployeesViewModel). "Empty"
/// representa "sin nada capturado todavía esa semana", no "se capturaron ceros".</summary>
public sealed record PayrollDeductionValues(decimal IsrAmount, decimal ImssAmount, decimal OtherAmount, string? OtherLabel, string? Notes)
{
    public static readonly PayrollDeductionValues Empty = new(0m, 0m, 0m, null, null);

    public static PayrollDeductionValues FromDomain(PayrollDeduction deduction) =>
        new(deduction.IsrAmount, deduction.ImssAmount, deduction.OtherAmount, deduction.OtherLabel, deduction.Notes);
}

/// <summary>Una insignia de un día en la grilla de Reportes — "Lun", "9:00"/"Descanso"/
/// "Falta"/"—" y el semáforo de puntualidad (ver <see cref="AttendanceColor"/> y
/// PunctualityClassifier) que decide de qué color se pinta.</summary>
public sealed record DayBadge(string DayLabel, string StatusText, AttendanceColor Color);

/// <summary>Una fila del reporte: el resultado de <see cref="WorkedHoursCalculator.CalculateWeek"/>
/// para un empleado, con su nombre y sucursal ya resueltos, más las deducciones (ISR/IMSS/
/// otro) capturadas manualmente para esa semana — ver comentario de clase de
/// <see cref="PayrollDeduction"/> sobre por qué el sistema nunca las calcula.</summary>
public sealed record PayrollRow(
    WeeklyPayrollSummary Summary, string EmployeeNumber, string EmployeeName, string BranchName, string? Department,
    PayrollDeductionValues Deductions)
{
    public bool HasWarnings => Summary.Warnings.Count > 0;
    public string WarningsText => string.Join(" | ", Summary.Warnings);

    // Texto propio en vez del StringFormat nativo de TimeSpan ("hh") — ese trunca a
    // 0-23 y separa los días aparte, y aquí puede haber más de 24h sumadas en la semana.
    public string RegularTimeText => FormatHoursAndMinutes(Summary.TotalRegularTime);
    public string OvertimeTimeText => FormatHoursAndMinutes(Summary.TotalOvertimeTime);

    /// <summary>"Pendiente" en vez de "$0.00" cuando el sueldo todavía no se capturó —
    /// StringFormat de WPF sobre un decimal? nulo se vería en blanco, no como una alerta
    /// clara; esto deja explícito que falta el dato, sin inventarlo como cero.</summary>
    public string WeeklySalaryText => Summary.WeeklySalary is { } salary ? salary.ToString("C") : "Pendiente";

    /// <summary>Una línea por día (lunes→domingo) con sus horas o su estado — ver regla
    /// de descanso/falta en el comentario de clase de WorkedHoursCalculator. Puramente
    /// informativo: el sueldo de arriba ya está completo tal cual, esto es para que el
    /// administrador vea de un vistazo qué ajustar a mano.</summary>
    public string DailyBreakdownText => string.Join(" | ", Summary.DailyBreakdown.Select(FormatDay));

    /// <summary>Cuántos días de la semana quedaron como falta (sin marcación, más allá
    /// del día de descanso permitido) — 0 no se resalta distinto, es solo un conteo para
    /// que el administrador sepa si hay algo que revisar antes de pagar.</summary>
    public int AbsenceCount => Summary.AbsenceCount;

    /// <summary>Una insignia por día (lunes→domingo) para pintar en la grilla — ver
    /// <see cref="DayBadge"/> y el semáforo de puntualidad en el comentario de clase de
    /// PunctualityClassifier. Puramente visual, la misma información que DailyBreakdownText.</summary>
    public IReadOnlyList<DayBadge> DailyBadges => [.. Summary.DailyBreakdown.Select(ToBadge)];

    private static DayBadge ToBadge(DailyAttendanceEntry day)
    {
        var statusText = day.Status switch
        {
            DayAttendanceStatus.Worked => FormatHoursAndMinutes(day.RegularTime + day.OvertimeTime),
            DayAttendanceStatus.RestDay => "Descanso",
            DayAttendanceStatus.Absence => "Falta",
            _ => "—",
        };

        return new DayBadge(GetDayAbbreviation(day.Date.DayOfWeek), statusText, day.Color);
    }

    private static string FormatDay(DailyAttendanceEntry day)
    {
        var statusText = day.Status switch
        {
            DayAttendanceStatus.Worked => FormatHoursAndMinutes(day.RegularTime + day.OvertimeTime),
            DayAttendanceStatus.RestDay => "Descanso",
            DayAttendanceStatus.Absence => "Falta",
            _ => "—",
        };

        return $"{GetDayAbbreviation(day.Date.DayOfWeek)} {statusText}";
    }

    private static string GetDayAbbreviation(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "Lun",
        DayOfWeek.Tuesday => "Mar",
        DayOfWeek.Wednesday => "Mié",
        DayOfWeek.Thursday => "Jue",
        DayOfWeek.Friday => "Vie",
        DayOfWeek.Saturday => "Sáb",
        _ => "Dom",
    };

    /// <summary>Bruto (Summary.TotalPay) menos las tres deducciones capturadas a mano —
    /// nunca se impide que salga negativo: el usuario capturó los montos, no hay nada que
    /// la app deba "corregir" aquí.</summary>
    public decimal NetPay => Summary.TotalPay - Deductions.IsrAmount - Deductions.ImssAmount - Deductions.OtherAmount;

    private static string FormatHoursAndMinutes(TimeSpan span) => $"{(int)span.TotalHours}:{span.Minutes:00}";
}

/// <summary>
/// ViewModel de la pantalla "Reportes": horas trabajadas + insumo de nómina por semana
/// (lunes a domingo, ver <see cref="WeekBoundary"/>) — combina ambas cosas en una sola
/// vista porque la nómina depende directamente de las horas calculadas.
///
/// Solo incluye empleados activos (no dados de baja, mismo criterio que
/// EmployeesViewModel oculta por defecto). Resuelve cada Attendance a un empleado con el
/// mismo criterio que AttendanceViewModel: EmployeeId directo, o si no hay, por
/// EmployeeDeviceMapping (DeviceId+DeviceUserPin).
///
/// El cálculo en sí (WorkedHoursCalculator) es lógica pura sin dependencias de este
/// ViewModel — aquí solo se junta la data (empleados activos + sus marcaciones de la
/// semana) y se muestra, incluyendo cualquier advertencia (columna "Advertencias") que el
/// cálculo haya generado — nunca se ocultan, ver comentario de clase de
/// WorkedHoursCalculator sobre los valores de PunchType no confirmados contra hardware real.
/// </summary>
public sealed partial class PayrollViewModel : ObservableObject
{
    private const int MaxAttendances = 5000;

    private readonly IEmployeeRepository _employeeRepository;
    private readonly IBranchRepository _branchRepository;
    private readonly IEmployeeDeviceMappingRepository _mappingRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IPayrollDeductionRepository _deductionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDeviceRepository _deviceRepository;
    private readonly DevicesViewModel _devicesViewModel;
    private readonly SupabaseAttendancePullService _cloudAttendancePull;

    /// <summary>Tope de espera de la consulta a Supabase al generar el reporte — sin esto,
    /// una red lenta dejaría la pantalla en "Calculando..." indefinidamente; si vence, se
    /// muestra lo que ya hay local y se avisa.</summary>
    private static readonly TimeSpan CloudPullTimeout = TimeSpan.FromSeconds(20);

    private const string AllBranchesOption = "Todas las sucursales";

    /// <summary>Resumen de qué se trajo del reloj/nube en la última carga (vacío si no se
    /// intentó traer nada) — se agrega al mensaje de estado en ApplyFilter para que el
    /// usuario vea por qué una semana sigue vacía (reloj desconectado, sin nube, etc.).</summary>
    private string _punchSourcesNote = "";

    private DateOnly _weekStart;

    /// <summary>Todas las filas calculadas para la semana actual, antes de aplicar
    /// SearchText/SelectedBranchFilter — mismo criterio que EmployeesViewModel._allRows:
    /// los filtros nunca vuelven a tocar la base de datos, solo se aplican en memoria
    /// sobre lo que ya se calculó (ver ApplyFilter).</summary>
    private List<PayrollRow> _allRows = [];

    [ObservableProperty]
    private string _statusMessage = "Cargando...";

    [ObservableProperty]
    private string _weekRangeText = "";

    /// <summary>Búsqueda por nombre o número de empleado, pedido explícito del usuario
    /// ("el filtro lo quiero... en reportes") para navegar la nómina del catálogo real de
    /// 54+ empleados sin desplazarse a mano.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Solo lista sucursales que de verdad tienen empleados en la semana actual —
    /// mismo criterio que EmployeesViewModel.BranchFilterOptions.</summary>
    public ObservableCollection<string> BranchFilterOptions { get; } = [AllBranchesOption];

    [ObservableProperty]
    private string _selectedBranchFilter = AllBranchesOption;

    public ObservableCollection<PayrollRow> PayrollRows { get; } = [];

    public PayrollViewModel(
        IEmployeeRepository employeeRepository, IBranchRepository branchRepository,
        IEmployeeDeviceMappingRepository mappingRepository, IAttendanceRepository attendanceRepository,
        IPayrollDeductionRepository deductionRepository, IUnitOfWork unitOfWork,
        IDeviceRepository deviceRepository, DevicesViewModel devicesViewModel,
        SupabaseAttendancePullService cloudAttendancePull)
    {
        _deviceRepository = deviceRepository;
        _devicesViewModel = devicesViewModel;
        _cloudAttendancePull = cloudAttendancePull;
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _mappingRepository = mappingRepository;
        _attendanceRepository = attendanceRepository;
        _deductionRepository = deductionRepository;
        _unitOfWork = unitOfWork;
        _weekStart = WeekBoundary.GetWeekStart(DateOnly.FromDateTime(DateTime.Now));
    }

    public async Task InitializeAsync() => await LoadAsync(pullPunches: true);

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadAsync(pullPunches: true);
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadAsync(pullPunches: true);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync(pullPunches: true);

    /// <summary>Guarda las deducciones (ISR/IMSS/Otro) de un empleado para la semana
    /// actualmente mostrada — busca-o-crea la fila de esa semana (nunca crea una segunda
    /// fila para "corregir" un monto ya capturado, ver PayrollDeduction.UpdateAmounts) y
    /// recarga la tabla para reflejar el cambio (incluye el "Neto a pagar" recalculado).
    /// Mismo patrón try/catch que EmployeesViewModel.UpdateMappingPinsAsync.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateDeductionsAsync(
        Guid employeeId, decimal isrAmount, decimal imssAmount, decimal otherAmount, string? otherLabel, string? notes)
    {
        try
        {
            var deduction = await _deductionRepository.GetByEmployeeAndWeekAsync(employeeId, _weekStart);
            if (deduction is null)
            {
                deduction = PayrollDeduction.Create(employeeId, _weekStart);
                await _deductionRepository.AddAsync(deduction);
            }

            deduction.UpdateAmounts(isrAmount, imssAmount, otherAmount, otherLabel, notes);
            await _unitOfWork.SaveChangesAsync();
            await LoadAsync();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudieron guardar las deducciones de nómina (EmployeeId={EmployeeId}, WeekStart={WeekStart}).",
                employeeId, _weekStart);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <param name="pullPunches">true al generar/actualizar el reporte (Actualizar, cambiar de
    /// semana, abrir la pantalla): antes de calcular, trae las marcaciones de la semana del
    /// reloj (si está conectado) y de Supabase (si hay nube configurada) — pedido explícito
    /// del usuario: "al generar un reporte trame las marcaciones que estoy solicitando". false
    /// al recargar solo por guardar deducciones (no hay motivo para volver a consultar).</param>
    private async Task LoadAsync(bool pullPunches = false)
    {
        var weekEnd = WeekBoundary.GetWeekEnd(_weekStart);
        WeekRangeText = $"{_weekStart:dd/MM/yyyy} – {weekEnd:dd/MM/yyyy}";
        StatusMessage = pullPunches ? "Trayendo marcaciones..." : "Calculando...";

        try
        {
            // Igual criterio que el resto de la app (ver AttendanceViewModel): sin
            // conversión real de zona horaria, se asume que el negocio opera en una sola.
            var fromUtc = DateTime.SpecifyKind(_weekStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(weekEnd.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

            if (pullPunches)
            {
                _punchSourcesNote = await PullMissingPunchesAsync(fromUtc, toUtc);
                StatusMessage = "Calculando...";
            }

            var employees = await _employeeRepository.ListAsync();
            var activeEmployees = employees.Where(e => e.Status != EmploymentStatus.Terminated).OrderBy(e => e.FullName).ToList();
            var branches = await _branchRepository.ListAsync();
            var mappings = await _mappingRepository.ListAsync();
            var attendances = await _attendanceRepository.ListAsync(fromUtc, toUtc, MaxAttendances);
            var deductions = await _deductionRepository.ListByWeekAsync(_weekStart);

            var branchNamesById = branches.ToDictionary(b => b.Id, b => b.Name);
            var employeeIdByDeviceAndPin = mappings.ToDictionary(m => (m.DeviceId, m.DeviceUserPin), m => m.EmployeeId);
            var attendancesByEmployeeId = GroupByResolvedEmployee(attendances, employeeIdByDeviceAndPin);
            var deductionsByEmployeeId = deductions.ToDictionary(d => d.EmployeeId, PayrollDeductionValues.FromDomain);

            var rows = new List<PayrollRow>();
            foreach (var employee in activeEmployees)
            {
                var employeeAttendances = attendancesByEmployeeId.TryGetValue(employee.Id, out var list)
                    ? (IReadOnlyList<Attendance>)list
                    : [];
                var summary = WorkedHoursCalculator.CalculateWeek(
                    employee, _weekStart, employeeAttendances, DateOnly.FromDateTime(DateTime.Now));
                var branchName = branchNamesById.TryGetValue(employee.BranchId, out var name) ? name : "(sucursal desconocida)";
                var deductionValues = deductionsByEmployeeId.TryGetValue(employee.Id, out var dv) ? dv : PayrollDeductionValues.Empty;
                rows.Add(new PayrollRow(summary, employee.Number.Value, employee.FullName, branchName, employee.Department, deductionValues));
            }

            _allRows = rows;

            // Reconstruye las opciones de sucursal a partir de quién tiene fila esta
            // semana — mismo criterio que EmployeesViewModel.ReloadAsync: nunca ofrecer
            // como filtro una sucursal que no aporta ninguna fila.
            var branchNamesWithRows = rows.Select(r => r.BranchName).Distinct().OrderBy(n => n).ToList();
            BranchFilterOptions.Clear();
            BranchFilterOptions.Add(AllBranchesOption);
            foreach (var name in branchNamesWithRows)
            {
                BranchFilterOptions.Add(name);
            }

            if (!BranchFilterOptions.Contains(SelectedBranchFilter))
            {
                SelectedBranchFilter = AllBranchesOption;
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo calcular la nómina de la semana ({WeekStart}).", _weekStart);
            StatusMessage = "No se pudo calcular la nómina. Revisa el registro de errores.";
        }
    }

    /// <summary>Completa la base local con las marcaciones del rango antes de calcular:
    /// primero el reloj (si hay uno conectado, ver DevicesViewModel.DownloadForReportAsync) y
    /// luego Supabase (lo que otra PC ya subió). Ninguna de las dos fuentes puede romper el
    /// reporte — cada falla se captura, se registra y se resume en la nota de estado; el
    /// cálculo siempre corre con lo que haya quedado en la base local.</summary>
    private async Task<string> PullMissingPunchesAsync(DateTime fromUtc, DateTime toUtc)
    {
        var parts = new List<string>();

        try
        {
            var (attempted, error, totalRead, savedCount) = await _devicesViewModel.DownloadForReportAsync();
            if (!attempted)
            {
                parts.Add("reloj no conectado");
            }
            else if (error is not null)
            {
                parts.Add($"no se pudo leer el reloj ({error})");
            }
            else
            {
                parts.Add($"reloj: {savedCount} nueva(s) de {totalRead} leída(s)");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudieron traer marcaciones del reloj para el reporte de la semana ({WeekStart}).", _weekStart);
            parts.Add("no se pudo leer el reloj");
        }

        if (!_cloudAttendancePull.IsConfigured)
        {
            parts.Add("nube sin configurar");
        }
        else
        {
            try
            {
                using var timeout = new CancellationTokenSource(CloudPullTimeout);
                var remote = await _cloudAttendancePull.FetchAsync(fromUtc, toUtc, timeout.Token);
                var (imported, skippedUnknownDevice) = await ImportCloudAttendancesAsync(remote, fromUtc, toUtc);
                var cloudNote = $"nube: {imported} nueva(s) de {remote.Count}";
                if (skippedUnknownDevice > 0)
                {
                    cloudNote += $", {skippedUnknownDevice} de un reloj no registrado en esta PC";
                }
                parts.Add(cloudNote);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudieron traer marcaciones de Supabase para el reporte de la semana ({WeekStart}).", _weekStart);
                parts.Add("no se pudo consultar la nube");
            }
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Guarda en la base local las marcaciones de Supabase que todavía no existen
    /// aquí. Conserva el Id de la nube (ver Attendance.Restore) para que la sincronización no
    /// las duplique al subir. Se omiten las de un dispositivo que esta PC no conoce (la
    /// marcación exige un DeviceId local válido); EmployeeId/BranchId se dejan en null si
    /// ese empleado/sucursal no existe localmente — el reporte igual las resuelve por
    /// DeviceId+PIN vía EmployeeDeviceMapping.</summary>
    private async Task<(int Imported, int SkippedUnknownDevice)> ImportCloudAttendancesAsync(
        IReadOnlyList<AttendanceDto> remote, DateTime fromUtc, DateTime toUtc)
    {
        if (remote.Count == 0)
        {
            return (0, 0);
        }

        var deviceIds = (await _deviceRepository.ListAsync()).Select(d => d.Id).ToHashSet();
        var branchIds = (await _branchRepository.ListAsync()).Select(b => b.Id).ToHashSet();
        var employeeIds = (await _employeeRepository.ListAsync()).Select(e => e.Id).ToHashSet();

        var local = await _attendanceRepository.ListAsync(fromUtc, toUtc, int.MaxValue);
        var knownIds = local.Select(a => a.Id).ToHashSet();
        var knownKeys = local.Select(a => (a.DeviceId, a.DeviceUserPin, a.TimestampUtc)).ToHashSet();

        var imported = 0;
        var skippedUnknownDevice = 0;
        foreach (var dto in remote)
        {
            if (!deviceIds.Contains(dto.DeviceId))
            {
                skippedUnknownDevice++;
                continue;
            }

            var timestamp = ToUtc(dto.TimestampUtc);
            var pin = dto.DeviceUserPin.Trim();
            if (knownIds.Contains(dto.Id) || knownKeys.Contains((dto.DeviceId, pin, timestamp)))
            {
                continue;
            }

            try
            {
                var attendance = Attendance.Restore(
                    dto.Id, dto.DeviceId,
                    dto.BranchId is { } branchId && branchIds.Contains(branchId) ? branchId : null,
                    dto.EmployeeId is { } employeeId && employeeIds.Contains(employeeId) ? employeeId : null,
                    pin, timestamp,
                    Enum.TryParse<AttendanceVerifyMethod>(dto.VerifyMethod, out var verifyMethod) ? verifyMethod : AttendanceVerifyMethod.Unknown,
                    dto.PunchType, dto.RawPayload,
                    ToUtc(dto.CreatedAtUtc), ToUtc(dto.UpdatedAtUtc), dto.ConcurrencyToken);
                await _attendanceRepository.AddAsync(attendance);
                knownIds.Add(attendance.Id);
                knownKeys.Add((attendance.DeviceId, attendance.DeviceUserPin, attendance.TimestampUtc));
                imported++;
            }
            catch (DomainException ex)
            {
                Log.Warning(ex, "Marcación de Supabase omitida por datos inválidos (Id={AttendanceId}).", dto.Id);
            }
        }

        if (imported > 0)
        {
            await _unitOfWork.SaveChangesAsync();
        }

        return (imported, skippedUnknownDevice);
    }

    /// <summary>System.Text.Json convierte a hora local cualquier fecha con offset que no
    /// sea "Z" (Supabase devuelve "+00:00") — como la app guarda "hora de pared etiquetada
    /// como UTC" (ver LoadAsync), hay que volver a UTC para no desplazar la marcación por la
    /// zona horaria de esta PC.</summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedBranchFilterChanged(string value) => ApplyFilter();

    /// <summary>Aplica SearchText + SelectedBranchFilter sobre _allRows, en memoria — igual
    /// criterio que EmployeesViewModel.ApplyVisibilityFilter, nunca vuelve a calcular la
    /// nómina por escribir en el buscador o cambiar la sucursal.</summary>
    private void ApplyFilter()
    {
        IEnumerable<PayrollRow> visible = _allRows;

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var term = SearchText.Trim();
            visible = visible.Where(row =>
                row.EmployeeName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                row.EmployeeNumber.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedBranchFilter != AllBranchesOption)
        {
            visible = visible.Where(row => row.BranchName == SelectedBranchFilter);
        }

        var visibleList = visible.ToList();

        PayrollRows.Clear();
        foreach (var row in visibleList)
        {
            PayrollRows.Add(row);
        }

        var warningCount = visibleList.Count(r => r.HasWarnings);
        var hiddenCount = _allRows.Count - visibleList.Count;

        StatusMessage = (warningCount > 0, hiddenCount > 0) switch
        {
            (true, true) => $"{visibleList.Count} de {_allRows.Count} empleado(s) — {warningCount} con advertencias, {hiddenCount} oculto(s) por los filtros.",
            (true, false) => $"{visibleList.Count} empleado(s) — {warningCount} con advertencias en su cálculo de horas (ver columna \"Advertencias\").",
            (false, true) => $"{visibleList.Count} de {_allRows.Count} empleado(s) — {hiddenCount} oculto(s) por los filtros aplicados.",
            (false, false) => $"{visibleList.Count} empleado(s).",
        };

        if (_punchSourcesNote.Length > 0)
        {
            StatusMessage += $" Marcaciones — {_punchSourcesNote}.";
        }
    }

    private static Dictionary<Guid, List<Attendance>> GroupByResolvedEmployee(
        IReadOnlyList<Attendance> attendances, Dictionary<(Guid DeviceId, string DeviceUserPin), Guid> employeeIdByDeviceAndPin)
    {
        var result = new Dictionary<Guid, List<Attendance>>();
        foreach (var attendance in attendances)
        {
            var resolvedEmployeeId = attendance.EmployeeId
                ?? (employeeIdByDeviceAndPin.TryGetValue((attendance.DeviceId, attendance.DeviceUserPin), out var eid) ? eid : (Guid?)null);
            if (resolvedEmployeeId is null)
            {
                continue;
            }

            if (!result.TryGetValue(resolvedEmployeeId.Value, out var list))
            {
                list = [];
                result[resolvedEmployeeId.Value] = list;
            }
            list.Add(attendance);
        }
        return result;
    }

    /// <summary>Arma el CSV de lo que está mostrando la tabla ahora mismo — mismo patrón
    /// que AttendanceViewModel.BuildCsv (el diálogo de guardar lo maneja PayrollView, el
    /// ViewModel no conoce tipos de WPF).</summary>
    public string BuildCsv()
    {
        var header = new[]
        {
            "Empleado", "Sucursal", "Departamento", "Checadas de la semana", "Faltas", "Horas normales",
            "Horas extra", "Sueldo semanal", "Pago horas extra", "Total a pagar", "ISR", "IMSS", "Otro (monto)",
            "Otro (concepto)", "Neto a pagar", "Notas de deducciones", "Advertencias",
        };
        var lines = new List<string> { string.Join(",", header.Select(CsvEscape)) };

        foreach (var row in PayrollRows)
        {
            var fields = new[]
            {
                row.EmployeeName,
                row.BranchName,
                row.Department ?? "",
                row.DailyBreakdownText,
                row.AbsenceCount.ToString(),
                row.RegularTimeText,
                row.OvertimeTimeText,
                row.Summary.WeeklySalary?.ToString("0.00") ?? "Pendiente",
                row.Summary.OvertimePay.ToString("0.00"),
                row.Summary.TotalPay.ToString("0.00"),
                row.Deductions.IsrAmount.ToString("0.00"),
                row.Deductions.ImssAmount.ToString("0.00"),
                row.Deductions.OtherAmount.ToString("0.00"),
                row.Deductions.OtherLabel ?? "",
                row.NetPay.ToString("0.00"),
                row.Deductions.Notes ?? "",
                row.WarningsText,
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
