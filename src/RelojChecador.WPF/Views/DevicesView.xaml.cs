using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using RelojChecador.Domain.Devices;
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
            dialog.SelectedBranch.Id, dialog.SelectedBranch.TimeZoneId, dialog.SerialNumber, dialog.MacAddress,
            dialog.CommunicationKey);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear el dispositivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnEditDeviceClick(object sender, RoutedEventArgs e)
    {
        // Este botón vive en el panel de diagnóstico (opera sobre SelectedDevice) — el de
        // la lista (OnEditDeviceListItemClick) resuelve primero el dispositivo de la fila y
        // llama a este mismo helper para no duplicar el flujo.
        if (DataContext is not DevicesViewModel viewModel || viewModel.SelectedDevice is null)
        {
            return;
        }

        await EditDeviceAsync(viewModel, viewModel.SelectedDevice);
    }

    /// <summary>"✏️ Editar" en la lista de dispositivos (columna izquierda) — antes solo se
    /// podía editar el dispositivo SELECCIONADO desde el panel de diagnóstico, sin ninguna
    /// acción directa en la propia lista. Selecciona la fila y reutiliza el mismo flujo.</summary>
    private async void OnEditDeviceListItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not Device device)
        {
            return;
        }

        viewModel.SelectedDevice = device;
        await EditDeviceAsync(viewModel, device);
    }

    private async Task EditDeviceAsync(DevicesViewModel viewModel, Device device)
    {
        var branches = await viewModel.GetBranchesAsync();
        var dialog = new EditDeviceDialog(device, branches, viewModel.HasCommunicationKey(device)) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.SelectedBranch is null)
        {
            return;
        }

        var error = await viewModel.UpdateDeviceAsync(
            dialog.DeviceId, dialog.DeviceName, dialog.Brand, dialog.Model, dialog.IpAddress, dialog.TcpPort,
            dialog.SelectedBranch.Id, dialog.SelectedBranch.TimeZoneId, dialog.SerialNumber, dialog.MacAddress,
            dialog.CommunicationKey, dialog.DeleteCommunicationKey);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo actualizar el dispositivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>"🗑️ Eliminar" en la lista de dispositivos — baja lógica (ver
    /// DevicesViewModel.DeleteDeviceAsync): el registro y su historial de asistencias se
    /// conservan, solo se oculta de la lista por defecto. El texto de confirmación lo deja
    /// claro para que nadie lo confunda con un borrado real — mismo criterio y misma
    /// redacción que OnDeleteEmployeeClick en EmployeesView.</summary>
    private async void OnDeleteDeviceListItemClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not Device device)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            $"¿Dar de baja el dispositivo \"{device.Name}\" ({device.IpAddress}:{device.TcpPort})? Se oculta de la lista y se detiene cualquier conexión activa, pero su historial de asistencias se conserva — puedes volver a verlo marcando \"Mostrar deshabilitados\".",
            "Dar de baja el dispositivo", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var error = await viewModel.DeleteDeviceAsync(device.Id);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo dar de baja el dispositivo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnDeviceUsersClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DevicesViewModel viewModel || viewModel.SelectedDevice is null)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Selecciona primero un dispositivo.",
                "Sin dispositivo seleccionado",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new DeviceUsersDialog(viewModel) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }
}
