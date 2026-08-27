using System.Windows;
using RelojChecador.Domain.Devices;

namespace RelojChecador.WPF.Views;

/// <summary>Diálogo mínimo para elegir un dispositivo cuando hay más de uno registrado —
/// usado por EmployeesView.OnSendEmployeesToDeviceClick (con un solo dispositivo, se usa
/// directo sin mostrar este diálogo).</summary>
public partial class SelectDeviceDialog : Window
{
    public Device? SelectedDevice => DeviceComboBox.SelectedItem as Device;

    public SelectDeviceDialog(IReadOnlyList<Device> devices)
    {
        InitializeComponent();
        DeviceComboBox.ItemsSource = devices;
        DeviceComboBox.SelectedIndex = 0;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnContinueClick(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is null)
        {
            ErrorTextBlock.Text = "Selecciona un dispositivo.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
