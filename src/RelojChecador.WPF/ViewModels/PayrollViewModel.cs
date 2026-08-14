using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.EmployeeDeviceMappings;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Payroll;
using RelojChecador.Domain.Attendances;
using RelojChecador.Domain.Common;
using RelojChecador.Domain.Employees;
using RelojChecador.Domain.Payroll;
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

/// <summary>Una fila del reporte: el resultado de <see cref="WorkedHoursCalculator.CalculateWeek"/>
/// para un empleado, con su nombre y sucursal ya resueltos, más las deducciones (ISR/IMSS/
/// otro) capturadas manualmente para esa semana — ver comentario de clase de
/// <see cref="PayrollDeduction"/> sobre por qué el sistema nunca las calcula.</summary>
public sealed record PayrollRow(WeeklyPayrollSummary Summary, string EmployeeName, string BranchName, PayrollDeductionValues Deductions)
{
    public bool HasWarnings => Summary.Warnings.Count > 0;
    public string WarningsText => string.Join(" | ", Summary.Warnings);

    // Texto propio en vez del StringFormat nativo de TimeSpan ("hh") — ese trunca a
    // 0-23 y separa los días aparte, y aquí puede haber más de 24h sumadas en la semana.
    public string RegularTimeText => FormatHoursAndMinutes(Summary.TotalRegularTime);
    public string OvertimeTimeText => FormatHoursAndMinutes(Summary.TotalOvertimeTime);

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

    private DateOnly _weekStart;

    [ObservableProperty]
    private string _statusMessage = "Cargando...";

    [ObservableProperty]
    private string _weekRangeText = "";

    public ObservableCollection<PayrollRow> PayrollRows { get; } = [];

    public PayrollViewModel(
        IEmployeeRepository employeeRepository, IBranchRepository branchRepository,
        IEmployeeDeviceMappingRepository mappingRepository, IAttendanceRepository attendanceRepository,
        IPayrollDeductionRepository deductionRepository, IUnitOfWork unitOfWork)
    {
        _employeeRepository = employeeRepository;
        _branchRepository = branchRepository;
        _mappingRepository = mappingRepository;
        _attendanceRepository = attendanceRepository;
        _deductionRepository = deductionRepository;
        _unitOfWork = unitOfWork;
        _weekStart = WeekBoundary.GetWeekStart(DateOnly.FromDateTime(DateTime.Now));
    }

    public async Task InitializeAsync() => await LoadAsync();

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync();

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

    private async Task LoadAsync()
    {
        var weekEnd = WeekBoundary.GetWeekEnd(_weekStart);
        WeekRangeText = $"{_weekStart:dd/MM/yyyy} – {weekEnd:dd/MM/yyyy}";
        StatusMessage = "Calculando...";

        try
        {
            // Igual criterio que el resto de la app (ver AttendanceViewModel): sin
            // conversión real de zona horaria, se asume que el negocio opera en una sola.
            var fromUtc = DateTime.SpecifyKind(_weekStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
            var toUtc = DateTime.SpecifyKind(weekEnd.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc);

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
                var summary = WorkedHoursCalculator.CalculateWeek(employee, _weekStart, employeeAttendances);
                var branchName = branchNamesById.TryGetValue(employee.BranchId, out var name) ? name : "(sucursal desconocida)";
                var deductionValues = deductionsByEmployeeId.TryGetValue(employee.Id, out var dv) ? dv : PayrollDeductionValues.Empty;
                rows.Add(new PayrollRow(summary, employee.FullName, branchName, deductionValues));
            }

            PayrollRows.Clear();
            foreach (var row in rows)
            {
                PayrollRows.Add(row);
            }

            var warningCount = rows.Count(r => r.HasWarnings);
            StatusMessage = warningCount > 0
                ? $"{rows.Count} empleado(s) — {warningCount} con advertencias en su cálculo de horas (ver columna \"Advertencias\")."
                : $"{rows.Count} empleado(s).";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo calcular la nómina de la semana ({WeekStart}).", _weekStart);
            StatusMessage = "No se pudo calcular la nómina. Revisa el registro de errores.";
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
            "Empleado", "Sucursal", "Horas normales", "Horas extra", "Sueldo semanal",
            "Pago horas extra", "Total a pagar", "ISR", "IMSS", "Otro (monto)", "Otro (concepto)",
            "Neto a pagar", "Notas de deducciones", "Advertencias",
        };
        var lines = new List<string> { string.Join(",", header.Select(CsvEscape)) };

        foreach (var row in PayrollRows)
        {
            var fields = new[]
            {
                row.EmployeeName,
                row.BranchName,
                row.RegularTimeText,
                row.OvertimeTimeText,
                row.Summary.WeeklySalary.ToString("0.00"),
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
