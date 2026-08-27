using System.ComponentModel;
using System.Linq;
using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Progreso en vivo de "Enviar empleados al reloj" (ver
/// EmployeesView.xaml.cs.OnSendEmployeesToDeviceClick y
/// DevicesViewModel.SendEmployeesToDeviceAsync) — pedido explícito del usuario tras
/// reportar que el envío masivo cerraba la app sin ningún aviso ni forma de saber si seguía
/// corriendo. Este diálogo NO ejecuta el envío por sí mismo: solo refleja el progreso real
/// que le reporta el ViewModel vía <see cref="DevicesViewModel.SendEmployeesProgress"/>
/// (un <c>Progress&lt;T&gt;</c> creado en el hilo de UI, que ya se encarga de que
/// <see cref="UpdateProgress"/> siempre corra ahí, nunca desde el hilo de fondo que hace el
/// envío de verdad) y expone <see cref="CancelRequested"/> para que quien lo abrió cancele
/// el <see cref="System.Threading.CancellationTokenSource"/> real detrás del envío.
///
/// La cancelación es cooperativa, nunca instantánea: el empleado que se esté enviando en
/// ese momento no se interrumpe a media llamada (no hay forma segura de hacerlo contra el
/// SDK nativo, ver ZKComWorker) — "Cancelar" detiene el bucle DESPUÉS de terminar con quien
/// esté en curso. Por eso ni el botón "Cancelar" ni el cierre de la ventana (✕/Alt+F4)
/// cierran el diálogo de inmediato mientras el envío sigue activo: solo piden la
/// cancelación y esperan a que <see cref="ShowCompleted"/> confirme que el envío ya se
/// detuvo de verdad.
/// </summary>
public partial class SendEmployeesProgressDialog : Window
{
    private bool _isRunning = true;
    private bool _cancelRequested;

    public event EventHandler? CancelRequested;

    public SendEmployeesProgressDialog(string deviceName)
    {
        InitializeComponent();
        DeviceTextBlock.Text = $"Reloj: {deviceName}";
    }

    /// <summary>Se llama desde el callback de un <c>Progress&lt;SendEmployeesProgress&gt;</c>
    /// — ya garantizado que corre en el hilo de UI, seguro para tocar controles.</summary>
    public void UpdateProgress(DevicesViewModel.SendEmployeesProgress progress)
    {
        var percent = progress.Total > 0 ? progress.Processed * 100.0 / progress.Total : 0;
        ProgressBarControl.Value = percent;

        if (!string.IsNullOrEmpty(progress.CurrentEmployeeName))
        {
            StatusTextBlock.Text = $"Enviando empleado {progress.Processed + 1} de {progress.Total}";
            CurrentEmployeeTextBlock.Text = progress.CurrentEmployeeName;
            CurrentEmployeeTextBlock.Visibility = Visibility.Visible;
        }
        else
        {
            CurrentEmployeeTextBlock.Visibility = Visibility.Collapsed;
        }

        SentCountTextBlock.Text = progress.Sent.ToString();
        FailedCountTextBlock.Text = progress.Failed.ToString();
        SkippedCountTextBlock.Text = progress.Skipped.ToString();
    }

    /// <summary>Reemplaza la barra/estado "en vivo" con el resumen final — pedido explícito
    /// del usuario ("Mensaje de finalización con resumen completo"). El botón pasa de
    /// "Cancelar" a "Cerrar" recién aquí, nunca antes.</summary>
    public void ShowCompleted(DevicesViewModel.SendEmployeesOutcome outcome)
    {
        _isRunning = false;

        ProgressBarControl.Value = outcome.Total > 0 ? 100 : 0;
        CurrentEmployeeTextBlock.Visibility = Visibility.Collapsed;
        SentCountTextBlock.Text = outcome.Sent.ToString();
        FailedCountTextBlock.Text = outcome.Failed.Count.ToString();
        SkippedCountTextBlock.Text = outcome.Skipped.ToString();

        StatusTextBlock.Text = outcome.Cancelled
            ? $"⏹️ Cancelado — {outcome.Sent} enviado(s), {outcome.Failed.Count} fallido(s), {outcome.Skipped} sin procesar de {outcome.Total}."
            : outcome.Failed.Count > 0
                ? $"⚠️ Terminado con errores — {outcome.Sent} de {outcome.Total} enviado(s) correctamente."
                : $"✅ Completado — {outcome.Sent} de {outcome.Total} empleado(s) enviados correctamente.";

        if (outcome.Failed.Count > 0)
        {
            FailuresListBox.ItemsSource = outcome.Failed.Select(f => $"{f.EmployeeName}: {f.Reason}").ToList();
            FailuresPanel.Visibility = Visibility.Visible;
            FailuresListBox.Visibility = Visibility.Visible;
        }

        CancelButton.Content = "Cerrar";
        CancelButton.IsEnabled = true;
    }

    /// <summary>Muestra un error de arranque (p. ej. no se pudo conectar) sin haber llegado
    /// siquiera a procesar un solo empleado — mismo botón "Cerrar" final, sin contadores ni
    /// barra (quedan en 0).</summary>
    public void ShowStartupError(string message)
    {
        _isRunning = false;
        StatusTextBlock.Text = $"⚠️ {message}";
        CancelButton.Content = "Cerrar";
        CancelButton.IsEnabled = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            RequestCancel();
            return;
        }

        DialogResult = true;
        Close();
    }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (!_isRunning)
        {
            return;
        }

        // El envío sigue activo — no se cierra de golpe (dejaría corriendo de fondo un
        // envío sin ningún diálogo que lo refleje), se trata igual que "Cancelar".
        e.Cancel = true;
        RequestCancel();
    }

    private void RequestCancel()
    {
        if (_cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        CancelButton.IsEnabled = false;
        StatusTextBlock.Text = "Cancelando... espera a que termine el empleado en curso.";
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }
}
