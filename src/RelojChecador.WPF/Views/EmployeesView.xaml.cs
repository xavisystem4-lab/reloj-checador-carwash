using System.Windows;
using System.Windows.Controls;
using RelojChecador.Domain.Devices;
using RelojChecador.WPF.ViewModels;
using Serilog;

namespace RelojChecador.WPF.Views;

public partial class EmployeesView : UserControl
{
    /// <summary>Referencia a la MISMA instancia de DevicesViewModel que usa la pestaña
    /// Dispositivos (ambas son Scoped a esta ventana, ver MainWindow.xaml.cs) — necesaria
    /// para "📤 Enviar empleados al reloj" (ver OnSendEmployeesToDeviceClick), que reutiliza
    /// su lógica de conectar/enviar/desconectar en vez de duplicarla aquí. No se pasa por
    /// DataContext porque el DataContext de esta vista es EmployeesViewModel — se asigna
    /// aparte, directo desde MainWindow.xaml.cs, justo como DeviceUsersDialog recibe
    /// DevicesViewModel sin pasar por binding.</summary>
    public DevicesViewModel? DevicesViewModel { get; set; }

    public EmployeesView()
    {
        InitializeComponent();
    }

    private void OnDeleteEmployeesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel)
        {
            return;
        }

        // Ofrece la misma lista que ya está visible en pantalla (respeta los filtros de
        // Buscar/Sucursal/Estatus/"Mostrar dados de baja" activos ahora mismo) — así se
        // puede filtrar por sucursal primero y borrar solo a esa gente.
        var dialog = new DeleteEmployeesDialog(viewModel, viewModel.Employees) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
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
            dialog.Number, dialog.FullName, dialog.SelectedBranch.Id, dialog.HireDate, dialog.WeeklySalary, dialog.Department, dialog.Position,
            dialog.OvertimeHourlyRate, dialog.SelectedDevice?.Id, dialog.DeviceUserPin, dialog.Notes);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear el empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnImportEmployeesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel)
        {
            return;
        }

        // A diferencia de los demás diálogos de esta pantalla, este SÍ recibe el
        // ViewModel directo — ver comentario de clase de ImportEmployeesDialog.
        var dialog = new ImportEmployeesDialog(viewModel) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    private void OnReplaceCatalogClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel)
        {
            return;
        }

        var dialog = new ReplaceEmployeeCatalogDialog(viewModel) { Owner = Window.GetWindow(this) };
        dialog.ShowDialog();
    }

    /// <summary>"📤 Enviar empleados al reloj" — movido aquí desde Dispositivos a pedido
    /// explícito del usuario. A diferencia de cuando vivía en Dispositivos (donde exigía
    /// que ya hubiera un dispositivo seleccionado y conectado a mano), este orquesta el
    /// flujo completo él solo sobre la MISMA instancia de DevicesViewModel: elegir
    /// dispositivo → Conectar → enviar → Desconectar — así queda todo dentro de un solo
    /// clic desde Empleados, sin tener que ir primero a la otra pestaña.</summary>
    private async void OnSendEmployeesToDeviceClick(object sender, RoutedEventArgs e)
    {
        if (DevicesViewModel is not { } devicesViewModel)
        {
            return;
        }

        if (devicesViewModel.Devices.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Primero registra al menos un dispositivo en la pestaña Dispositivos.",
                "Sin dispositivos", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Device targetDevice;
        if (devicesViewModel.Devices.Count == 1)
        {
            targetDevice = devicesViewModel.Devices[0];
        }
        else
        {
            var pickDialog = new SelectDeviceDialog(devicesViewModel.Devices) { Owner = Window.GetWindow(this) };
            if (pickDialog.ShowDialog() != true || pickDialog.SelectedDevice is null)
            {
                return;
            }
            targetDevice = pickDialog.SelectedDevice;
        }

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            $"¿Enviar TODOS los empleados activos (de cualquier sucursal) a \"{targetDevice.Name}\"?\n\n" +
            "Esto se conecta al dispositivo y sube Nombre + PIN (asignado en automático) de quien todavía " +
            "no esté vinculado — la conexión se queda activa después (igual que si hubieras presionado " +
            "\"Conectar\" en Dispositivos), no se desconecta sola. Solo prepara el PIN — la huella se " +
            "enrola físicamente en el dispositivo. La sucursal de cada empleado sigue siendo la que tiene " +
            "en su expediente — no depende de a qué reloj esté enrolado.",
            "Enviar empleados al reloj", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var button = (Button)sender;
        button.IsEnabled = false;
        try
        {
            devicesViewModel.SelectedDevice = targetDevice;
            await devicesViewModel.ConnectCommand.ExecuteAsync(null);

            if (!devicesViewModel.IsConnected)
            {
                MessageBox.Show(
                    Window.GetWindow(this),
                    $"No se pudo conectar con \"{targetDevice.Name}\". Revisa la pestaña Dispositivos para más detalle " +
                    "(Bitácora / diagnóstico de conexión) y vuelve a intentar.",
                    "No se pudo conectar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // A propósito NO se desconecta al terminar — reportado por el usuario:
            // desconectar aquí también suspendía el auto-reconnect (mismo comportamiento
            // que "Desconectar" en Dispositivos, ver DevicesViewModel.DisconnectAsync),
            // así que "Usuarios del reloj" (u otra acción en Dispositivos) inmediatamente
            // después pedía conectar de nuevo. Se deja la conexión activa, igual que si el
            // usuario hubiera presionado "Conectar" él mismo.
            await RunSendWithProgressDialogAsync(devicesViewModel, targetDevice);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>Abre el diálogo de progreso y lo mantiene sincronizado con el envío real —
    /// pedido explícito del usuario tras reportar que el envío masivo (55 empleados) cerraba
    /// la app sin ningún aviso ni forma de saber si seguía corriendo. El envío en sí corre
    /// en segundo plano (ver DevicesViewModel.SendEmployeesToDeviceAsync, que ya encola cada
    /// llamada al SDK en su propio hilo dedicado — ver ZKComWorker — así que esta ventana
    /// nunca se congela mientras tanto); este método solo conecta ese progreso real con la
    /// UI del diálogo, nunca lo simula.
    ///
    /// <see cref="System.Windows.Window.ShowDialog"/> bombea mensajes internamente aunque
    /// "bloquee" esta línea de código — por eso <paramref name="devicesViewModel"/>'s envío
    /// (ya arrancado antes de llamarlo) sigue avanzando y reportando progreso mientras el
    /// diálogo está abierto, y por qué <see cref="WatchSendTaskAsync"/> se lanza ANTES de
    /// <c>ShowDialog</c>: captura el <see cref="System.Threading.SynchronizationContext"/> de
    /// UI en ese punto para que su "await" final vuelva a este mismo hilo sin importar el
    /// bucle anidado de <c>ShowDialog</c>.</summary>
    private async Task RunSendWithProgressDialogAsync(DevicesViewModel devicesViewModel, Device targetDevice)
    {
        using var cts = new CancellationTokenSource();
        var dialog = new SendEmployeesProgressDialog(targetDevice.Name) { Owner = Window.GetWindow(this) };
        dialog.CancelRequested += (_, _) => cts.Cancel();

        var progress = new Progress<DevicesViewModel.SendEmployeesProgress>(dialog.UpdateProgress);
        var sendTask = devicesViewModel.SendEmployeesToDeviceAsync(progress, cts.Token);

        _ = WatchSendTaskAsync(sendTask, dialog);
        dialog.ShowDialog();
    }

    /// <summary>Espera el resultado real del envío y actualiza el diálogo — separado de
    /// <see cref="RunSendWithProgressDialogAsync"/> para poder lanzarlo como
    /// "fire-and-forget" ANTES de <c>ShowDialog</c> (ver comentario de ese método).
    /// SendEmployeesToDeviceAsync ya atrapa cualquier excepción inesperada y la devuelve
    /// como <see cref="DevicesViewModel.SendEmployeesOutcome.Error"/> en vez de lanzarla —
    /// el <c>catch</c> de aquí es un resguardo adicional (requisito explícito: "ninguna
    /// excepción debe cerrar la aplicación"), no la ruta esperada.</summary>
    private static async Task WatchSendTaskAsync(Task<DevicesViewModel.SendEmployeesOutcome> sendTask, SendEmployeesProgressDialog dialog)
    {
        try
        {
            var outcome = await sendTask;
            if (outcome.Success)
            {
                dialog.ShowCompleted(outcome);
            }
            else
            {
                dialog.ShowStartupError(outcome.Error ?? "No se pudo enviar.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error inesperado no atrapado por SendEmployeesToDeviceAsync al enviar empleados al reloj.");
            dialog.ShowStartupError("Ocurrió un error inesperado. Revisa el registro de errores.");
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
            dialog.Phone, dialog.Email, dialog.SelectedStatus, dialog.WeeklySalary, dialog.OvertimeHourlyRate,
            dialog.SelectedDevice?.Id, dialog.DeviceUserPin, dialog.Notes);

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

    private async void OnEditMappingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not EmployeeRow row)
        {
            return;
        }

        var mappings = await viewModel.GetMappingsForEmployeeAsync(row.Employee.Id);
        if (mappings.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "Este empleado todavía no está vinculado a ningún dispositivo — usa \"Vincular a dispositivo\" primero.",
                "Sin vínculos que editar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new EditEmployeeMappingsDialog(row.Employee.FullName, mappings) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.ChangedPins.Count == 0)
        {
            return;
        }

        var error = await viewModel.UpdateMappingPinsAsync(dialog.ChangedPins);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo corregir el vínculo", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void OnDeleteEmployeeClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not EmployeesViewModel viewModel || (sender as FrameworkElement)?.DataContext is not EmployeeRow row)
        {
            return;
        }

        // Baja lógica (ver EmployeesViewModel.DeleteEmployeeAsync): el registro y su
        // historial se conservan, solo se oculta de la lista por defecto — el texto de
        // confirmación lo deja claro para que nadie lo confunda con un borrado real.
        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            $"¿Dar de baja a \"{row.Employee.FullName}\"? Se oculta de la lista, pero su historial de asistencias y vínculos se conserva — puedes volver a verlo marcando \"Mostrar dados de baja\".",
            "Dar de baja al empleado", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var error = await viewModel.DeleteEmployeeAsync(row.Employee.Id);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo dar de baja al empleado", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
