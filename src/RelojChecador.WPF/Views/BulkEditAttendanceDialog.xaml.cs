using System.Windows;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Elige Entrada/Salida para aplicar a varias marcaciones seleccionadas de un solo golpe —
/// pedido explícito del usuario: "editarlo masivamente con un check, escoger a los empleados
/// y ponerle si es entrado o salida". El guardado real vive en
/// AttendanceViewModel.BulkSetPunchTypeAsync; este diálogo solo captura el tipo elegido.
/// </summary>
public partial class BulkEditAttendanceDialog : Window
{
    public int PunchType => EntradaRadioButton.IsChecked == true ? 0 : 1;

    public BulkEditAttendanceDialog(int selectedCount)
    {
        InitializeComponent();
        CountTextBlock.Text = $"Se {(selectedCount == 1 ? "va a aplicar" : "van a aplicar")} el mismo tipo a {selectedCount} marcación(es) seleccionada(s).";
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnApplyClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
