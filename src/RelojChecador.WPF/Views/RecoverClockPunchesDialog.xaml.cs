using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Una opción del combo "Qué hacer" de <see cref="RecoverClockPunchesDialog"/>.</summary>
public sealed record OrphanPinOption(string Display, DevicesViewModel.OrphanPinAction Action, Guid? EmployeeId);

/// <summary>Envuelve un <see cref="DevicesViewModel.OrphanClockPin"/> con checkbox, la acción
/// elegida y el resultado — mismo patrón que <see cref="SelectableUnresolvedPinRow"/>.</summary>
public sealed partial class SelectableOrphanPinRow : ObservableObject
{
    public DevicesViewModel.OrphanClockPin Pin { get; }
    public IReadOnlyList<OrphanPinOption> Options { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private OrphanPinOption? _selectedOption;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Ya se trajo al sistema — no se vuelve a aplicar.</summary>
    public bool IsDone { get; set; }

    public DateTime LastSeenLocal => Pin.LastSeenUtc.ToLocalTime();

    /// <summary>Nombre del reloj, o el del empleado dueño de las marcaciones.</summary>
    public string WhoDisplay =>
        Pin.ClockName ?? Pin.TerminatedOwner?.FullName ?? Pin.ActiveMappingOwner?.FullName ?? "(sin nombre)";

    public SelectableOrphanPinRow(DevicesViewModel.OrphanClockPin pin, IReadOnlyList<Employee> activeEmployees)
    {
        Pin = pin;

        var options = new List<OrphanPinOption>();
        if (pin.ActiveMappingOwner is { } mapped)
        {
            options.Add(new($"🔗 Es {mapped.Number.Value} · {mapped.FullName} (ya tiene este PIN)",
                DevicesViewModel.OrphanPinAction.LinkExisting, mapped.Id));
        }
        if (pin.TerminatedOwner is { } terminated)
        {
            options.Add(new($"♻️ Reactivar a {terminated.FullName} con número nuevo",
                DevicesViewModel.OrphanPinAction.ReactivateTerminated, null));
        }
        else if (pin.ActiveMappingOwner is null)
        {
            options.Add(new($"➕ Dar de alta como empleado nuevo ({pin.ClockName ?? $"PIN {pin.Pin}"})",
                DevicesViewModel.OrphanPinAction.CreateNew, null));
        }
        options.AddRange(activeEmployees
            .Where(e => e.Id != pin.ActiveMappingOwner?.Id)
            .Select(e => new OrphanPinOption($"🔗 Es {e.Number.Value} · {e.FullName}", DevicesViewModel.OrphanPinAction.LinkExisting, e.Id)));

        Options = options;
        _selectedOption = options.FirstOrDefault();
        _isSelected = pin.LastSeenUtc >= DateTime.UtcNow.AddDays(-30);
    }
}

/// <summary>
/// "📥 Traer marcaciones del reloj" — pedido explícito del usuario: "hay empleados que no
/// aparecen en el sistema pero sí checaron en el reloj físico". Quien lo abre (EmployeesView)
/// ya descargó las marcaciones del reloj; aquí se listan los PINs cuyas marcaciones no ve
/// nadie (ver DevicesViewModel.GetOrphanClockPinsAsync) y, por fila, se reactiva al empleado
/// dado de baja, se da de alta uno nuevo o se atribuye a uno existente
/// (DevicesViewModel.ResolveOrphanClockPinAsync).
/// </summary>
public partial class RecoverClockPunchesDialog : Window
{
    private readonly DevicesViewModel _devicesViewModel;
    private readonly EmployeesViewModel _employeesViewModel;
    private ObservableCollection<SelectableOrphanPinRow> _rows = [];
    private bool _suppressSelectAllEvent;

    /// <summary>¿Se trajo al menos un PIN? — quien abrió el diálogo recarga Empleados.</summary>
    public bool AnyApplied { get; private set; }

    public RecoverClockPunchesDialog(DevicesViewModel devicesViewModel, EmployeesViewModel employeesViewModel, string downloadSummary)
    {
        InitializeComponent();
        _devicesViewModel = devicesViewModel;
        _employeesViewModel = employeesViewModel;
        DownloadSummaryTextBlock.Text = downloadSummary;
        Loaded += async (_, _) => await LoadRowsAsync();
    }

    private async Task LoadRowsAsync()
    {
        var pins = await _devicesViewModel.GetOrphanClockPinsAsync();
        var activeEmployees = (await _employeesViewModel.ListLinkableEmployeesAsync())
            .OrderBy(e => e.Number.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _rows = [.. pins.Select(p => new SelectableOrphanPinRow(p, activeEmployees))];
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateSelectionState();
        }
        PinsGrid.ItemsSource = _rows;

        if (_rows.Count == 0)
        {
            DownloadSummaryTextBlock.Text += "\n✅ Todas las marcaciones del reloj ya pertenecen a un empleado vigente.";
        }
        UpdateSelectionState();
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllEvent)
        {
            return;
        }

        var select = SelectAllCheckBox.IsChecked == true;
        foreach (var row in _rows.Where(r => !r.IsDone))
        {
            row.IsSelected = select;
        }
        UpdateSelectionState();
    }

    private void OnRowCheckedChanged(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        var selectable = _rows.Where(r => !r.IsDone).ToList();
        var selectedCount = selectable.Count(r => r.IsSelected);
        SelectedCountTextBlock.Text = $"{selectedCount} seleccionado(s) de {selectable.Count}";
        ApplyButton.IsEnabled = selectedCount > 0;

        _suppressSelectAllEvent = true;
        SelectAllCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == selectable.Count ? true : null;
        _suppressSelectAllEvent = false;
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        // En orden de PIN, para que los números nuevos (EMP-055, EMP-056…) sigan ese mismo orden.
        var selected = _rows
            .Where(r => r.IsSelected && !r.IsDone && r.SelectedOption is not null)
            .OrderBy(r => int.TryParse(r.Pin.Pin, out var n) ? n : int.MaxValue)
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var reactivations = selected.Count(r => r.SelectedOption!.Action != DevicesViewModel.OrphanPinAction.LinkExisting);
        var confirmed = MessageBox.Show(
            this,
            $"¿Traer al sistema {selected.Count} PIN(s)?\n\n" +
            (reactivations > 0 ? $"{reactivations} persona(s) se darán de alta de nuevo con el siguiente número libre.\n" : "") +
            "Sus marcaciones pasarán a aparecer en Asistencia, Reportes y el Dashboard web.",
            "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        var ok = 0;
        var failed = 0;
        foreach (var row in selected)
        {
            row.StatusText = "Procesando...";
            var option = row.SelectedOption!;
            var error = await _devicesViewModel.ResolveOrphanClockPinAsync(row.Pin, option.Action, option.EmployeeId);
            if (error is null)
            {
                row.StatusText = "✅ Listo";
                row.IsDone = true;
                row.IsSelected = false;
                ok++;
            }
            else
            {
                row.StatusText = $"❌ {error}";
                failed++;
            }
        }

        if (ok > 0)
        {
            AnyApplied = true;
            await _devicesViewModel.TriggerCloudSyncAsync();
        }

        UpdateSelectionState();
        MessageBox.Show(
            this,
            $"{ok} PIN(s) traído(s) al sistema, {failed} con error. Revisa la columna \"Estado\" para el detalle." +
            (ok > 0 ? "\n\nSiguiente paso sugerido: \"Coincidir PIN con número\" para que su PIN sea igual a su número nuevo." : ""),
            "Traer marcaciones", MessageBoxButton.OK, failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = AnyApplied;
}
