using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para corregir el nombre y el estatus habilitado de un usuario ya
/// existente en el reloj físico. El PIN se muestra de solo lectura a propósito — ver
/// comentario de DevicesViewModel.UpdateDeviceUserAsync sobre por qué no se edita aquí.
/// </summary>
public partial class EditDeviceUserDialog : Window
{
    public string NewName => NameTextBox.Text.Trim();
    public bool NewIsEnabled => IsEnabledCheckBox.IsChecked == true;

    public EditDeviceUserDialog(DeviceUserRow row)
    {
        InitializeComponent();
        PinTextBox.Text = row.DeviceUserPin;
        NameTextBox.Text = row.Name;
        IsEnabledCheckBox.IsChecked = row.IsEnabled;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorTextBlock.Text = "El nombre es obligatorio.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
