using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Envuelve un <see cref="EmployeeRow"/> ya cargado en pantalla con un checkbox de
/// selección — EmployeeRow es un record inmutable (no puede llevar su propio IsSelected
/// mutable con notificación de cambios), así que este wrapper vive solo mientras el diálogo
/// está abierto, nunca se guarda en ningún lado.</summary>
public sealed partial class SelectableEmployeeRow(EmployeeRow row) : ObservableObject
{
    public EmployeeRow Row { get; } = row;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// "Borrar" — a diferencia del botón "Eliminar" por fila (baja lógica, ver
/// EmployeesViewModel.DeleteEmployeeAsync), este diálogo hace un borrado FÍSICO y permanente
/// de los empleados que se seleccionen (ver EmployeesViewModel.HardDeleteEmployeesAsync).
/// Pedido explícito del usuario: poder elegir a quiénes borrar (con opción de "Seleccionar
/// todo"), antes de importar un catálogo nuevo y no toparse con conflictos.
///
/// La lista que se ofrece para borrar es la MISMA que está visible ahora mismo en la
/// pantalla de Empleados (respeta los filtros de Buscar/Sucursal/Estatus/"Mostrar dados de
/// baja" ya aplicados) — así se puede, por ejemplo, filtrar por una sucursal primero y
/// borrar solo a esa gente, sin tener que revisar el catálogo completo.
/// </summary>
public partial class DeleteEmployeesDialog : Window
{
    private readonly EmployeesViewModel _viewModel;
    private readonly ObservableCollection<SelectableEmployeeRow> _rows;
    private bool _suppressSelectAllEvent;

    public DeleteEmployeesDialog(EmployeesViewModel viewModel, IEnumerable<EmployeeRow> employees)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _rows = [.. employees.Select(e => new SelectableEmployeeRow(e))];
        foreach (var row in _rows)
        {
            row.PropertyChanged += (_, _) => UpdateSelectionState();
        }
        EmployeesGrid.ItemsSource = _rows;
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
        DeleteButton.IsEnabled = selectedCount > 0;

        _suppressSelectAllEvent = true;
        SelectAllCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == _rows.Count ? true : null;
        _suppressSelectAllEvent = false;
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var names = string.Join("\n", selected.Take(10).Select(r => $"• {r.Row.Employee.FullName}"));
        var moreText = selected.Count > 10 ? $"\n… y {selected.Count - 10} más." : "";

        var confirmed = MessageBox.Show(
            this,
            $"¿Borrar PERMANENTEMENTE {selected.Count} empleado(s)?\n\n{names}{moreText}\n\n" +
            "Se borran junto con sus vínculos de PIN al reloj y su historial de deducciones de nómina. " +
            "Sus marcaciones de asistencia NO se borran, pero quedan sin vincular otra vez. " +
            "Esto NO se puede deshacer.",
            "Confirmar borrado permanente",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        DeleteButton.IsEnabled = false;
        var employeeIds = selected.Select(r => r.Row.Employee.Id).ToList();
        var outcome = await _viewModel.HardDeleteEmployeesAsync(employeeIds);

        if (!outcome.Success)
        {
            MessageBox.Show(this, outcome.Error, "No se pudo borrar", MessageBoxButton.OK, MessageBoxImage.Warning);
            DeleteButton.IsEnabled = true;
            return;
        }

        MessageBox.Show(
            this,
            $"Listo: {outcome.EmployeesDeleted} empleado(s) borrado(s), {outcome.MappingsDeleted} vínculo(s) de PIN " +
            $"eliminado(s), {outcome.PayrollDeductionsDeleted} deducción(es) de nómina eliminada(s), " +
            $"{outcome.AttendancesUnlinked} marcación(es) desvinculada(s) (no borradas).",
            "Borrado completado", MessageBoxButton.OK, MessageBoxImage.Information);

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
