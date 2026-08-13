using System.Windows;
using System.Windows.Controls;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class EmployeesView : UserControl
{
    public EmployeesView()
    {
        InitializeComponent();
    }

    private async void OnAddEmployeeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel)
        {
            return;
        }

        var branches = await viewModel.GetBranchesAsync();
        if (branches.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Primero registra al menos una sucursal antes de agregar un empleado.",
                "No hay sucursales", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new AddEmployeeDialog(branches) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedBranch is null)
        {
            return;
        }

        var error = await viewModel.CreateEmployeeAsync(
            dialog.Number, dialog.FullName, dialog.SelectedBranch.Id, dialog.HireDate, dialog.Department, dialog.Position);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear el empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnLinkDeviceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not EmployeeRow row)
        {
            return;
        }

        var devices = await viewModel.GetDevicesAsync();
        if (devices.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Primero registra al menos un dispositivo antes de vincular un empleado.",
                "No hay dispositivos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new LinkEmployeeDeviceDialog(row.Employee.FullName, devices) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedDevice is null)
        {
            return;
        }

        var error = await viewModel.CreateMappingAsync(row.Employee.Id, dialog.SelectedDevice.Id, dialog.DeviceUserPin);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo vincular el empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
