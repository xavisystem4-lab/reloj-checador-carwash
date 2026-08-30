using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using RelojChecador.Application.Attendances;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Application.Employees;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Common;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel de la ventana principal: lista y permite crear sucursales. Es la primera
/// pantalla funcional real (Fase 4) — la navegación completa con las demás secciones
/// (Empleados, Dispositivos, Asistencia, etc. — Fase 3 del diseño visual) todavía no
/// existe; esta es la base sobre la que se construyen las siguientes.
///
/// No conoce tipos de WPF (Window, Dialog): quien la usa (MainWindow, en el code-behind)
/// es responsable de mostrar el diálogo y le pasa los datos capturados ya como texto.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IBranchRepository _branchRepository;
    private readonly IEmployeeRepository _employeeRepository;
    private readonly IDeviceRepository _deviceRepository;
    private readonly IAttendanceRepository _attendanceRepository;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Lista completa sin filtrar — <see cref="Branches"/> se reconstruye a partir
    /// de esta aplicando <see cref="ApplyBranchVisibilityFilter"/>, mismo criterio que
    /// DevicesViewModel._allDevices/EmployeesViewModel._allRows: "eliminar" una sucursal
    /// (ver <see cref="DeleteBranchAsync"/>) es baja lógica, nunca borra el registro, así
    /// que por defecto se oculta pero puede volver a mostrarse sin recargar la base.</summary>
    private List<Branch> _allBranches = [];

    [ObservableProperty]
    private string _statusMessage = "Cargando información local...";

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _showInactiveBranches;

    public ObservableCollection<Branch> Branches { get; } = [];

    public MainViewModel(
        IBranchRepository branchRepository, IEmployeeRepository employeeRepository, IDeviceRepository deviceRepository,
        IAttendanceRepository attendanceRepository, IUnitOfWork unitOfWork)
    {
        _branchRepository = branchRepository;
        _employeeRepository = employeeRepository;
        _deviceRepository = deviceRepository;
        _attendanceRepository = attendanceRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            _allBranches = (await _branchRepository.ListAsync()).ToList();
            ApplyBranchVisibilityFilter();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo cargar la lista de sucursales al iniciar la ventana principal.");
            StatusMessage = "No se pudo cargar la información local. Revisa el registro de errores.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnShowInactiveBranchesChanged(bool value) => ApplyBranchVisibilityFilter();

    private void ApplyBranchVisibilityFilter()
    {
        IEnumerable<Branch> visible = ShowInactiveBranches
            ? _allBranches
            : _allBranches.Where(b => b.IsActive);

        Branches.Clear();
        foreach (var branch in visible)
        {
            Branches.Add(branch);
        }

        RefreshStatusMessage();
    }

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> CreateBranchAsync(
        string code, string name, string timeZoneId, string? legalEntityName, string? address)
    {
        try
        {
            var branch = Branch.Create(code, name, timeZoneId, legalEntityName, address);
            await _branchRepository.AddAsync(branch);
            await _unitOfWork.SaveChangesAsync();

            _allBranches.Add(branch);
            ApplyBranchVisibilityFilter();
            return null;
        }
        catch (DomainException ex)
        {
            // Ej.: código/nombre vacíos — validación de negocio, mensaje ya comprensible.
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            // El índice único de Branch.Code (ver EfBranchRepository/BranchConfiguration) es
            // lo que normalmente dispara esto — se interpreta como duplicado sin inspeccionar
            // el texto del error nativo de SQLite, que no es estable entre versiones.
            Log.Warning(ex, "No se pudo guardar la sucursal, probablemente por código duplicado (Code={Code})", code);
            return "Ya existe una sucursal con ese código.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al crear una sucursal (Code={Code})", code);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> UpdateBranchAsync(
        Guid branchId, string code, string name, string timeZoneId, string? legalEntityName, string? address, bool isActive)
    {
        try
        {
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch is null)
            {
                return "No se encontró la sucursal — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            // ChangeCode dispara el índice único (ver catch de abajo) — solo se llama si
            // de verdad cambió, para no arriesgar el mismo choque al re-guardar sin tocar
            // el código.
            if (!string.Equals(branch.Code, code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                branch.ChangeCode(code);
            }

            branch.Rename(name);
            branch.UpdateTimeZone(timeZoneId);
            branch.UpdateLegalInfo(legalEntityName, address);
            if (isActive)
            {
                branch.Activate();
            }
            else
            {
                branch.Deactivate();
            }

            await _unitOfWork.SaveChangesAsync();

            _allBranches = (await _branchRepository.ListAsync()).ToList();
            ApplyBranchVisibilityFilter();
            return null;
        }
        catch (DomainException ex)
        {
            return ex.Message;
        }
        catch (DbUpdateException ex)
        {
            Log.Warning(ex, "No se pudo actualizar la sucursal, probablemente por código duplicado (BranchId={BranchId}, Code={Code})", branchId, code);
            return "Ya existe una sucursal con ese código.";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al actualizar la sucursal (BranchId={BranchId})", branchId);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    /// <summary>"Eliminar" una sucursal — baja lógica, igual criterio que
    /// DevicesViewModel.DeleteDeviceAsync/EmployeesViewModel.DeleteEmployeeAsync: el
    /// registro se conserva (con sus empleados/dispositivos ya vinculados, que siguen
    /// referenciando su Id), solo se marca inactiva y se oculta de la lista por defecto —
    /// nunca se borra de verdad.</summary>
    /// <returns>Un mensaje de error comprensible si algo salió mal, o null si se guardó correctamente.</returns>
    public async Task<string?> DeleteBranchAsync(Guid branchId)
    {
        try
        {
            var branch = await _branchRepository.GetByIdAsync(branchId);
            if (branch is null)
            {
                return "No se encontró la sucursal — puede que la lista esté desactualizada. Cierra y vuelve a abrir esta pantalla.";
            }

            branch.Deactivate();
            await _unitOfWork.SaveChangesAsync();

            _allBranches = (await _branchRepository.ListAsync()).ToList();
            ApplyBranchVisibilityFilter();
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al dar de baja una sucursal (BranchId={BranchId})", branchId);
            return "Ocurrió un error inesperado al guardar. Revisa el registro de errores.";
        }
    }

    public sealed record HardDeleteBranchesOutcome(
        int BranchesDeleted, int EmployeesReassigned, int DevicesReassigned, int AttendancesReassigned, string? Error)
    {
        public bool Success => Error is null;
    }

    /// <summary>Borrado FÍSICO y permanente de una o más sucursales — a diferencia de
    /// <see cref="DeleteBranchAsync"/> (baja lógica), esto SÍ elimina el registro de
    /// verdad. Pedido explícito del usuario para consolidar varias sucursales de prueba en
    /// una sola, conservando el dato de dónde trabajaba cada quien.
    ///
    /// Por cada sucursal a borrar (nunca <paramref name="targetBranchId"/>, esa se protege
    /// aunque venga incluida por error):
    /// - Sus empleados se REASIGNAN a <paramref name="targetBranchId"/> (Employee.TransferToBranch)
    ///   — nunca se borran ni se dan de baja. Si el empleado no tiene ya un Departamento
    ///   capturado, se le guarda ahí el nombre de la sucursal original (Branch.Name), para
    ///   no perder ese dato aunque administrativamente ya no exista esa sucursal — pedido
    ///   explícito: "que en el reporte salga que empleado es de cada sucursal".
    /// - Sus marcaciones de asistencia se actualizan al mismo destino
    ///   (Attendance.ReconcileEmployee), igual criterio que el resto de la app: "la sucursal
    ///   de una marcación siempre se deriva del empleado, nunca es un dato independiente".
    /// - Sus dispositivos (relojes) se reasignan también, para no dejarlos sin sucursal.
    /// - Al final se borra la sucursal.
    /// Todo en un solo SaveChangesAsync.</summary>
    public async Task<HardDeleteBranchesOutcome> HardDeleteBranchesAsync(IReadOnlyList<Guid> branchIds, Guid targetBranchId)
    {
        try
        {
            var targetBranch = await _branchRepository.GetByIdAsync(targetBranchId);
            if (targetBranch is null)
            {
                return new HardDeleteBranchesOutcome(0, 0, 0, 0, "No se encontró la sucursal destino — cierra y vuelve a abrir esta pantalla.");
            }

            var employeesReassigned = 0;
            var devicesReassigned = 0;
            var attendancesReassigned = 0;
            var branchesDeleted = 0;

            foreach (var branchId in branchIds)
            {
                if (branchId == targetBranchId)
                {
                    continue; // nunca te borras a ti mismo, aunque venga marcada por error
                }

                var branch = await _branchRepository.GetByIdAsync(branchId);
                if (branch is null)
                {
                    continue; // ya no existe (lista desactualizada) — se sigue con el resto
                }

                foreach (var employee in await _employeeRepository.ListByBranchAsync(branchId))
                {
                    var department = string.IsNullOrWhiteSpace(employee.Department) ? branch.Name : employee.Department;
                    employee.UpdatePersonalInfo(employee.FullName, department, employee.Position);
                    employee.TransferToBranch(targetBranchId);
                    employeesReassigned++;

                    foreach (var attendance in await _attendanceRepository.ListByEmployeeAsync(employee.Id))
                    {
                        attendance.ReconcileEmployee(employee.Id, targetBranchId);
                        attendancesReassigned++;
                    }
                }

                foreach (var device in await _deviceRepository.ListByBranchAsync(branchId))
                {
                    device.UpdateDetails(
                        device.Name, device.Brand, device.Model, targetBranchId, device.TimeZoneId,
                        device.SerialNumber, device.MacAddress);
                    devicesReassigned++;
                }

                await _branchRepository.RemoveAsync(branch);
                branchesDeleted++;
            }

            await _unitOfWork.SaveChangesAsync();
            _allBranches = (await _branchRepository.ListAsync()).ToList();
            ApplyBranchVisibilityFilter();
            return new HardDeleteBranchesOutcome(branchesDeleted, employeesReassigned, devicesReassigned, attendancesReassigned, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado al borrar sucursales físicamente (Count={Count})", branchIds.Count);
            return new HardDeleteBranchesOutcome(0, 0, 0, 0, "Ocurrió un error inesperado al borrar. Revisa el registro de errores.");
        }
    }

    private void RefreshStatusMessage()
    {
        var hiddenCount = _allBranches.Count - Branches.Count;
        if (_allBranches.Count == 0)
        {
            StatusMessage = "Aún no hay sucursales registradas.";
        }
        else if (hiddenCount > 0)
        {
            StatusMessage = $"{Branches.Count} de {_allBranches.Count} sucursal(es) — {hiddenCount} oculta(s) por estar inactiva(s).";
        }
        else
        {
            StatusMessage = $"{Branches.Count} sucursal(es) registrada(s) en la base local.";
        }
    }

    /// <summary>Arma el CSV de las sucursales visibles ahora mismo — mismo patrón que
    /// AttendanceViewModel.BuildCsv/PayrollViewModel.BuildCsv (el diálogo de guardar lo
    /// maneja BranchesView, el ViewModel no conoce tipos de WPF).</summary>
    public string BuildCsv()
    {
        var header = new[] { "Código", "Nombre", "Zona horaria", "Razón social", "Activa" };
        var lines = new List<string> { string.Join(",", header.Select(CsvEscape)) };

        foreach (var branch in Branches)
        {
            var fields = new[]
            {
                branch.Code,
                branch.Name,
                branch.TimeZoneId,
                branch.LegalEntityName ?? "",
                branch.IsActive ? "Sí" : "No",
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
