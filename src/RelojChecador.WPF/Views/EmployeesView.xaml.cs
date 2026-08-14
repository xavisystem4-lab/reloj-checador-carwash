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

        // Se pasan también los dispositivos (aunque puedan venir vacíos) para que el
        // diálogo permita vincular al reloj checador en la misma alta — ver
        // AddEmployeeDialog, checkbox "Vincular a un reloj checador ahora".
        var devices = await viewModel.GetDevicesAsync();

        var dialog = new AddEmployeeDialog(branches, devices) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedBranch is null)
        {
            return;
        }

        var error = await viewModel.CreateEmployeeAsync(
            dialog.Number, dialog.FullName, dialog.SelectedBranch.Id, dialog.HireDate, dialog.Department, dialog.Position,
            dialog.SelectedDevice?.Id, dialog.DeviceUserPin);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear el empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnEditEmployeeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not EmployeeRow row)
        {
            return;
        }

        var branches = await viewModel.GetBranchesAsync();
        var devices = await viewModel.GetDevicesAsync();
        var dialog = new EditEmployeeDialog(row, branches, devices) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedBranch is null)
        {
            return;
        }

        var error = await viewModel.UpdateEmployeeAsync(
            row.Employee.Id, dialog.Number, dialog.FullName, dialog.SelectedBranch.Id, dialog.Department, dialog.Position,
            dialog.Phone, dialog.Email, dialog.SelectedStatus, dialog.SelectedDevice?.Id, dialog.DeviceUserPin);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo editar el empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
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
