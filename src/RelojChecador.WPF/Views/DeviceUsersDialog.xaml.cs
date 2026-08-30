using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Ver/editar/eliminar (individual o en lote) los usuarios dados de alta directamente en
/// la memoria del reloj físico — pedido explícito del usuario tras "Consultar información"
/// (que solo mostraba el conteo total, sin el detalle). Recibe DevicesViewModel directo
/// (no solo captura datos y deja que el code-behind del llamador orqueste después) porque,
/// igual que ImportEmployeesDialog, es un flujo interactivo de varios pasos (consultar →
/// editar/eliminar → refrescar) que necesita quedarse abierto entre cada acción.
/// </summary>
public partial class DeviceUsersDialog : Window
{
    private readonly DevicesViewModel _viewModel;

    public DeviceUsersDialog(DevicesViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        UsersDataGrid.ItemsSource = _viewModel.DeviceUsers;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async System.Threading.Tasks.Task RefreshAsync()
    {
        await _viewModel.LoadDeviceUsersAsync();
        StatusTextBlock.Text = _viewModel.DeviceUsersStatusMessage;
        SelectAllCheckBox.IsChecked = false;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnBulkRenumberClick(object sender, RoutedEventArgs e)
    {
        var dialog = new BulkRenumberDevicePinsDialog(_viewModel, _viewModel.DeviceUsers) { Owner = this };
        dialog.ShowDialog();
        await RefreshAsync(); // por si algo se movió, refleja el estado real más reciente
    }

    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        var selectAll = SelectAllCheckBox.IsChecked == true;
        foreach (var row in _viewModel.DeviceUsers)
        {
            row.IsSelected = selectAll;
        }
    }

    private async void OnEditUserClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceUserRow row)
        {
            return;
        }

        var dialog = new EditDeviceUserDialog(row) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = await _viewModel.UpdateDeviceUserAsync(row, dialog.NewName, dialog.NewIsEnabled);
        if (error is not null)
        {
            MessageBox.Show(this, error, "No se pudo actualizar el usuario", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        StatusTextBlock.Text = _viewModel.DeviceUsersStatusMessage;
    }

    private async void OnChangePinClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceUserRow row)
        {
            return;
        }

        var dialog = new ChangeDeviceUserPinDialog(row) { Owner = this };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        // Confirmación final aparte del diálogo (que ya trae su propia advertencia) —
        // mismo criterio que las eliminaciones de abajo: una acción sobre el reloj físico
        // que toca una huella ya enrolada merece un segundo "¿seguro?" explícito, no solo
        // el botón "Cambiar PIN" del formulario.
        var confirmed = MessageBox.Show(
            this,
            $"¿Mover a \"{row.Name}\" del PIN {row.DeviceUserPin} al PIN {dialog.NewPin}? Esto copia su huella al PIN nuevo y borra el PIN viejo del reloj.",
            "Confirmar cambio de PIN",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var error = await _viewModel.ChangeDeviceUserPinAsync(row, dialog.NewPin);
        StatusTextBlock.Text = _viewModel.DeviceUsersStatusMessage;

        if (error is not null)
        {
            MessageBox.Show(this, error, "No se pudo cambiar el PIN", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteUserClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not DeviceUserRow row)
        {
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"¿Eliminar a \"{row.Name}\" (PIN {row.DeviceUserPin}) del reloj?\n\n" +
            "Esto borra su huella y sus datos del dispositivo físico — no se puede deshacer. " +
            "Su historial de asistencia ya guardado NO se borra.",
            "Confirmar eliminación",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var (deleted, failed) = await _viewModel.DeleteDeviceUsersAsync([row]);
        StatusTextBlock.Text = _viewModel.DeviceUsersStatusMessage;

        if (failed.Count > 0)
        {
            MessageBox.Show(this, failed[0], "No se pudo eliminar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteSelectedClick(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.DeviceUsers.Where(u => u.IsSelected).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "No hay ningún usuario seleccionado.", "Nada que eliminar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirm = MessageBox.Show(
            this,
            $"¿Eliminar {selected.Count} usuario(s) del reloj?\n\n" +
            "Esto borra su huella y sus datos del dispositivo físico — no se puede deshacer. " +
            "Su historial de asistencia ya guardado NO se borra.",
            "Confirmar eliminación masiva",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        var (deleted, failed) = await _viewModel.DeleteDeviceUsersAsync(selected);
        StatusTextBlock.Text = _viewModel.DeviceUsersStatusMessage;

        if (failed.Count > 0)
        {
            MessageBox.Show(
                this,
                $"Se eliminaron {deleted} de {selected.Count}. Fallaron:\n\n{string.Join("\n", failed)}",
                "Eliminación parcial",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
