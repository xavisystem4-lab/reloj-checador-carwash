using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Envuelve un <see cref="EmployeesViewModel.UnresolvedPinRow"/> con un checkbox de
/// selección y el empleado elegido para vincularlo — mismo patrón que
/// SelectableEmployeeRow/SelectableBranchRow en los diálogos de borrado, pero acá cada fila
/// necesita ADEMÁS su propio combo (no todas se vinculan al mismo destino).</summary>
public sealed partial class SelectableUnresolvedPinRow(EmployeesViewModel.UnresolvedPinRow row, IReadOnlyList<Employee> employees) : ObservableObject
{
    public EmployeesViewModel.UnresolvedPinRow Row { get; } = row;
    public IReadOnlyList<Employee> Employees { get; } = employees;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private Employee? _selectedEmployee;
}

/// <summary>
/// "Vincular pendientes" — pedido explícito del usuario: "Habilita la opción para Vincular
/// de manera masiva que podamos seleccionar todos o de 1 por 1". Lista cada combinación
/// (dispositivo, PIN) que todavía tiene marcaciones sin vincular a ningún empleado (ver
/// EmployeesViewModel.GetUnresolvedPinsAsync), deja elegir el empleado por fila con un combo,
/// y vincula (EmployeesViewModel.CreateMappingAsync — mismo método que usa "Vincular a
/// dispositivo" por empleado, con su misma conciliación retroactiva) a quien tenga checkbox
/// marcado Y empleado elegido.
/// </summary>
public partial class LinkUnresolvedPinsDialog : Window
{
    private readonly EmployeesViewModel _viewModel;
    private ObservableCollection<SelectableUnresolvedPinRow> _rows = [];
    private bool _suppressSelectAllEvent;

    public LinkUnresolvedPinsDialog(EmployeesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        Loaded += async (_, _) => await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        var pins = await _viewModel.GetUnresolvedPinsAsync();
        var employees = await _viewModel.ListLinkableEmployeesAsync();

        // Sugerencia automática — pedido explícito del usuario: "que busque
        // automáticamente a donde se va vincular con la información que ya tiene el
        // software". La única correspondencia que el sistema ya conoce con certeza (no es
        // una suposición nueva) es Employee.Number == PIN: la misma convención que se
        // estableció para todo el catálogo ("quiero que se respete tal cual la numeración
        // del PIN"), usada al generar Pin=Number en cada importación desde entonces. Solo se
        // sugiere si hay EXACTAMENTE un empleado con ese número — nunca se adivina entre
        // varios ni se inventa una coincidencia parcial. La fila queda igual de editable:
        // esto solo rellena el combo, la persona sigue pudiendo cambiarlo o dejarlo vacío
        // antes de confirmar.
        var employeeByNumber = employees
            .GroupBy(e => e.Number.Value, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);

        _rows = [.. pins.Select(p =>
        {
            var row = new SelectableUnresolvedPinRow(p, employees);
            if (employeeByNumber.TryGetValue(p.DeviceUserPin, out var suggested))
            {
                row.SelectedEmployee = suggested;
            }
            return row;
        })];
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateSelectionState();
        }
        PinsGrid.ItemsSource = _rows;
        UpdateSelectionState();
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllEvent)
        {
            return;
        }

        var select = SelectAllCheckBox.IsChecked == true;
        foreach (var row in _rows)
        {
            row.IsSelected = select;
        }
        UpdateSelectionState();
    }

    private void OnRowCheckedChanged(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        var selectedCount = _rows.Count(r => r.IsSelected);
        SelectedCountTextBlock.Text = $"{selectedCount} seleccionado(s) de {_rows.Count}";
        LinkButton.IsEnabled = selectedCount > 0;

        _suppressSelectAllEvent = true;
        SelectAllCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == _rows.Count ? true : null;
        _suppressSelectAllEvent = false;
    }

    private async void OnLinkClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var withoutEmployee = selected.Count(r => r.SelectedEmployee is null);
        var toLink = selected.Where(r => r.SelectedEmployee is not null).ToList();
        if (toLink.Count == 0)
        {
            MessageBox.Show(
                this, "Ninguna de las filas seleccionadas tiene un empleado elegido en el combo \"Vincular a\".",
                "Nada que vincular", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LinkButton.IsEnabled = false;
        var linked = 0;
        var errors = new List<string>();
        foreach (var row in toLink)
        {
            var employee = row.SelectedEmployee!;
            var error = await _viewModel.CreateMappingAsync(employee.Id, row.Row.DeviceId, row.Row.DeviceUserPin);
            if (error is null)
            {
                linked++;
            }
            else
            {
                errors.Add($"PIN {row.Row.DeviceUserPin} ({row.Row.DeviceName}) → {employee.FullName}: {error}");
            }
        }

        await ReloadAsync();

        var summary = $"{linked} vinculado(s).";
        if (withoutEmployee > 0)
        {
            summary += $"\n{withoutEmployee} marcado(s) sin elegir empleado — se dejaron pendientes.";
        }
        if (errors.Count > 0)
        {
            summary += "\n\nErrores:\n" + string.Join("\n", errors);
        }

        MessageBox.Show(this, summary, "Vinculación completada", MessageBoxButton.OK, MessageBoxImage.Information);
        LinkButton.IsEnabled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
