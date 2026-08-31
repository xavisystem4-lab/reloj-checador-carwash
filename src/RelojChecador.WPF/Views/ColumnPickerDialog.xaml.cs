using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RelojChecador.WPF.Views;

/// <summary>Una fila del selector: la clave estable de la columna (x:Name en
/// EmployeesView.xaml), el texto que ve el usuario, y si está marcada. El ORDEN dentro de
/// <see cref="ColumnPickerDialog.Rows"/> ES el orden elegido — no hay un campo aparte para
/// eso.</summary>
public sealed partial class ColumnPickerRow(string key, string displayName, bool isVisible) : ObservableObject
{
    public string Key { get; } = key;
    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool _isVisible = isVisible;
}

/// <summary>
/// "🔧 Columnas" en Empleados — pedido explícito del usuario: "me gustaría tener un botón o
/// una opción de yo escoger qué columnas quiero y cómo las quiero acomodar". Recibe el
/// estado ACTUAL de las columnas (orden + visibilidad, ya resuelto por EmployeesView.xaml.cs
/// a partir de EmployeesGrid.Columns en vivo) y devuelve el nuevo estado elegido —
/// aplicar/guardar de verdad vive en EmployeesView.xaml.cs, este diálogo solo captura la
/// intención.
/// </summary>
public partial class ColumnPickerDialog : Window
{
    public ObservableCollection<ColumnPickerRow> Rows { get; }
    public bool ResetRequested { get; private set; }

    public ColumnPickerDialog(IReadOnlyList<ColumnPickerRow> rows)
    {
        InitializeComponent();
        Rows = [.. rows];
        ColumnsListBox.ItemsSource = Rows;
    }

    private void OnMoveUpClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ColumnPickerRow row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        if (index > 0)
        {
            Rows.Move(index, index - 1);
        }
    }

    private void OnMoveDownClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ColumnPickerRow row)
        {
            return;
        }

        var index = Rows.IndexOf(row);
        if (index < Rows.Count - 1)
        {
            Rows.Move(index, index + 1);
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        ResetRequested = true;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnApplyClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
