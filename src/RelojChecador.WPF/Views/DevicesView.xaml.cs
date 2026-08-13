using System.Windows;
using System.Windows.Controls;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class DevicesView : UserControl
{
    public DevicesView()
    {
        InitializeComponent();
    }

    private async void OnAddDeviceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel)
        {
            return;
        }

        var branches = await viewModel.GetBranchesAsync();
        if (branches.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Primero registra al menos una sucursal antes de agregar un dispositivo.",
                "No hay sucursales",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new AddDeviceDialog(branches) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedBranch is null)
        {
            return;
        }

        var error = await viewModel.CreateDeviceAsync(
            dialog.DeviceName, dialog.Brand, dialog.Model, dialog.IpAddress, dialog.TcpPort,
            dialog.SelectedBranch.Id, dialog.SelectedBranch.TimeZoneId, dialog.SerialNumber, dialog.MacAddress);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear el dispositivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
