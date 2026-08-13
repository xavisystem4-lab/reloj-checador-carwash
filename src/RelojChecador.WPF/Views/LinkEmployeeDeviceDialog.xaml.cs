using System.Windows;
using RelojChecador.Domain.Devices;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para vincular un empleado ya existente a un dispositivo, capturando a
/// mano el PIN que ese reloj usa internamente para reconocerlo — no se conecta al
/// dispositivo para descargarlo (ver comentario de EmployeesViewModel). La validación real
/// de negocio (duplicados, ver los dos índices únicos de EmployeeDeviceMapping) la hace la
/// base al guardar; aquí solo se evita mandar campos obviamente vacíos.
/// </summary>
public partial class LinkEmployeeDeviceDialog : Window
{
    public Device? SelectedDevice => DeviceComboBox.SelectedItem as Device;
    public string DeviceUserPin => PinTextBox.Text.Trim();

    public LinkEmployeeDeviceDialog(string employeeFullName, IReadOnlyList<Device> devices)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Vincular a {employeeFullName}";

        DeviceComboBox.ItemsSource = devices;
        if (devices.Count > 0)
        {
            DeviceComboBox.SelectedIndex = 0;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is null || string.IsNullOrWhiteSpace(DeviceUserPin))
        {
            ShowError("Dispositivo y PIN son obligatorios.");
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
