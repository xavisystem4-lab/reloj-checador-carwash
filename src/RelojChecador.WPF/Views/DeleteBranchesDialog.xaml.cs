using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Domain.Branches;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Envuelve una Branch ya cargada en pantalla con un checkbox de selección — mismo
/// patrón que SelectableEmployeeRow en DeleteEmployeesDialog. <see cref="CanSelect"/> es
/// false para la sucursal elegida como destino: no tiene sentido poder marcarla para
/// borrarla a la vez que es donde se reasigna todo lo demás.</summary>
public sealed partial class SelectableBranchRow(Branch branch) : ObservableObject
{
    public Branch Branch { get; } = branch;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _canSelect = true;
}

/// <summary>
/// "Borrar sucursales" — a diferencia de "Eliminar" por fila (baja lógica, ver
/// MainViewModel.DeleteBranchAsync), este diálogo hace un borrado FÍSICO y permanente de las
/// sucursales que se seleccionen, reasignando automáticamente sus empleados, dispositivos y
/// marcaciones a la sucursal destino elegida (ver MainViewModel.HardDeleteBranchesAsync).
/// Pedido explícito del usuario para consolidar varias sucursales de prueba en una sola.
/// </summary>
public partial class DeleteBranchesDialog : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ObservableCollection<SelectableBranchRow> _rows;
    private bool _suppressSelectAllEvent;

    public DeleteBranchesDialog(MainViewModel viewModel, IEnumerable<Branch> branches)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _rows = [.. branches.Select(b => new SelectableBranchRow(b))];
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateSelectionState();
        }
        BranchesGrid.ItemsSource = _rows;

        TargetBranchComboBox.ItemsSource = _rows.Select(r => r.Branch).ToList();
        TargetBranchComboBox.SelectedIndex = 0;

        UpdateSelectionState();
    }

    private void OnTargetBranchChanged(object sender, SelectionChangedEventArgs e)
    {
        var target = TargetBranchComboBox.SelectedItem as Branch;
        foreach (var row in _rows)
        {
            row.CanSelect = row.Branch.Id != target?.Id;
            if (!row.CanSelect)
            {
                row.IsSelected = false; // no dejes marcada para borrar a la que ahora es el destino
            }
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
        foreach (var row in _rows.Where(r => r.CanSelect))
        {
            row.IsSelected = select;
        }
        UpdateSelectionState();
    }

    private void OnRowCheckedChanged(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private void UpdateSelectionState()
    {
        var selectable = _rows.Where(r => r.CanSelect).ToList();
        var selectedCount = selectable.Count(r => r.IsSelected);
        SelectedCountTextBlock.Text = $"{selectedCount} seleccionada(s) de {selectable.Count}";
        DeleteButton.IsEnabled = selectedCount > 0;

        _suppressSelectAllEvent = true;
        SelectAllCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == selectable.Count ? true : null;
        _suppressSelectAllEvent = false;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (TargetBranchComboBox.SelectedItem is not Branch target)
        {
            return;
        }

        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var names = string.Join("\n", selected.Take(10).Select(r => $"• {r.Branch.Name}"));
        var moreText = selected.Count > 10 ? $"\n… y {selected.Count - 10} más." : "";

        var confirmed = MessageBox.Show(
            this,
            $"¿Borrar PERMANENTEMENTE {selected.Count} sucursal(es)?\n\n{names}{moreText}\n\n" +
            $"Sus empleados, dispositivos y marcaciones se reasignan a \"{target.Name}\". " +
            "Esto NO se puede deshacer.",
            "Confirmar borrado permanente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteButton.IsEnabled = false;
        var branchIds = selected.Select(r => r.Branch.Id).ToList();
        var outcome = await _viewModel.HardDeleteBranchesAsync(branchIds, target.Id);

        if (!outcome.Success)
        {
            MessageBox.Show(this, outcome.Error, "No se pudo borrar", MessageBoxButton.OK, MessageBoxImage.Warning);
            DeleteButton.IsEnabled = true;
            return;
        }

        MessageBox.Show(
            this,
            $"Listo: {outcome.BranchesDeleted} sucursal(es) borrada(s). {outcome.EmployeesReassigned} empleado(s), " +
            $"{outcome.DevicesReassigned} dispositivo(s) y {outcome.AttendancesReassigned} marcación(es) reasignados a \"{target.Name}\".",
            "Borrado completado", MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
