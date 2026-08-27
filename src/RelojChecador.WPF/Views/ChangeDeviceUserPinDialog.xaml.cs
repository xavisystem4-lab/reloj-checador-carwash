using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para pedir el PIN nuevo al "mover" un usuario del reloj (huella incluida)
/// de un PIN a otro — ver DevicesViewModel.ChangeDeviceUserPinAsync para el flujo completo
/// y por qué el orden de los pasos importa. Solo captura el dato y muestra la advertencia;
/// toda la lógica de mover la huella vive en el ViewModel, no aquí.
/// </summary>
public partial class ChangeDeviceUserPinDialog : Window
{
    public string NewPin => NewPinTextBox.Text.Trim();

    public ChangeDeviceUserPinDialog(DeviceUserRow row)
    {
        InitializeComponent();
        CurrentPinTextBox.Text = row.DeviceUserPin;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewPin))
        {
            ErrorTextBlock.Text = "El PIN nuevo es obligatorio.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
