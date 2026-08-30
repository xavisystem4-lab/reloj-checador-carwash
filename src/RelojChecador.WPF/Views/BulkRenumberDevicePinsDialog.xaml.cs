using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Devices;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Envuelve un <see cref="DeviceUserRow"/> con el PIN destino y el estado de la
/// operación — mismo patrón de checkbox que los demás diálogos de selección masiva, más una
/// columna de progreso que se va llenando mientras corre (mover una huella no es
/// instantáneo). El PIN destino arranca sugerido con el Número del empleado vinculado, pero
/// es EDITABLE — pedido explícito del usuario: "habilita la opción para poder colocar el PIN
/// destino manualmente para ajustar todo de un jalón" (caso real: alguien sin folio numérico
/// válido, o que se quiera mandar a un PIN distinto del sugerido).</summary>
public sealed partial class SelectableRepinRow : ObservableObject
{
    public DeviceUserRow Row { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _statusText = "";

    /// <summary>Editable desde la columna "PIN destino" del DataGrid — arranca con el Número
    /// del empleado vinculado si es un PIN válido (dígitos) y distinto del PIN actual; vacío
    /// si no hay sugerencia (sin folio numérico, o ya coincide). NotifyPropertyChangedFor
    /// para que HasValidTarget (y por tanto si el checkbox se puede marcar) se actualice al
    /// instante mientras la persona escribe, no solo al perder el foco.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasValidTarget))]
    private string? _targetPin;

    public bool HasValidTarget =>
        !string.IsNullOrWhiteSpace(TargetPin) && TargetPin.All(char.IsDigit) && TargetPin != Row.DeviceUserPin;

    public SelectableRepinRow(DeviceUserRow row)
    {
        Row = row;
        var suggested = row.LinkedEmployeeNumber;
        _targetPin = !string.IsNullOrWhiteSpace(suggested) && suggested.All(char.IsDigit) && suggested != row.DeviceUserPin
            ? suggested
            : null;
    }
}

/// <summary>
/// "Renumerar PINs del reloj" — pedido explícito del usuario: quiere que el PIN real de cada
/// persona en el dispositivo sea igual a su Número de empleado ("Número 1 PIN 1 Adrian
/// Uribe, Número 2 PIN 2 Angel David..."), no solo el folio del software. Mueve la huella YA
/// enrolada de cada persona (DevicesViewModel.ChangeDeviceUserPinAsync, el mismo método que
/// ya usa el botón "Cambiar PIN" individual — nunca probado contra hardware real antes de
/// esto, ver su comentario de clase) al PIN = su Número.
///
/// Procesa en orden dependiente-seguro: en cada vuelta solo mueve a quien su PIN destino
/// esté LIBRE en el reloj en ESE momento (consultando el estado real, no una foto vieja) —
/// mover a alguien libera su PIN viejo, lo que puede destrabar a otra persona en la siguiente
/// vuelta. Si queda alguien atorado en un ciclo real (A necesita el PIN de B y B necesita el
/// de A) se reporta aparte en vez de forzar un movimiento doble arriesgado.
/// </summary>
public partial class BulkRenumberDevicePinsDialog : Window
{
    private readonly DevicesViewModel _viewModel;
    private ObservableCollection<SelectableRepinRow> _rows = [];
    private bool _suppressSelectAllEvent;

    public BulkRenumberDevicePinsDialog(DevicesViewModel viewModel, IEnumerable<DeviceUserRow> deviceUsers)
    {
        InitializeComponent();
        _viewModel = viewModel;

        _rows = [.. deviceUsers.Select(u => new SelectableRepinRow(u))];
        foreach (var row in _rows)
        {
            row.IsSelected = row.HasValidTarget;
            RefreshPendingStatusText(row);
            row.PropertyChanged += (_, e) =>
            {
                // Refleja en vivo un PIN destino escrito a mano (ver TargetPin) — solo
                // mientras la fila sigue "pendiente"/inválida, nunca pisa el resultado real
                // de una fila que ya se procesó (✅/❌/⏸️).
                if (e.PropertyName == nameof(SelectableRepinRow.TargetPin))
                {
                    RefreshPendingStatusText(row);
                }
                UpdateSelectionState();
            };
        }
        RowsGrid.ItemsSource = _rows;
        UpdateSelectionState();
    }

    private void OnSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressSelectAllEvent)
        {
            return;
        }

        var select = SelectAllCheckBox.IsChecked == true;
        foreach (var row in _rows.Where(r => r.HasValidTarget))
        {
            row.IsSelected = select;
        }
        UpdateSelectionState();
    }

    private void OnRowCheckedChanged(object sender, RoutedEventArgs e) => UpdateSelectionState();

    private static readonly string[] ProcessedStatusMarkers = ["✅", "❌", "⏸️", "Moviendo"];

    private static void RefreshPendingStatusText(SelectableRepinRow row)
    {
        if (ProcessedStatusMarkers.Any(marker => row.StatusText.Contains(marker)))
        {
            return; // ya se intentó de verdad — nunca pisar ese resultado con un texto genérico
        }

        row.StatusText = row.HasValidTarget ? "Pendiente" : "Sin folio numérico válido o ya coincide — no se puede mover";
    }

    private void UpdateSelectionState()
    {
        var selectable = _rows.Where(r => r.HasValidTarget).ToList();
        var selectedCount = selectable.Count(r => r.IsSelected);
        SelectedCountTextBlock.Text = $"{selectedCount} seleccionado(s) de {selectable.Count}";
        ApplyButton.IsEnabled = selectedCount > 0;

        _suppressSelectAllEvent = true;
        SelectAllCheckBox.IsChecked = selectedCount == 0 ? false : selectedCount == selectable.Count ? true : null;
        _suppressSelectAllEvent = false;
    }

    private async void OnApplyClick(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected && r.HasValidTarget).ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            $"¿Mover la huella de {selected.Count} persona(s) a su PIN nuevo?\n\n" +
            "Esta operación toca el reloj físico de verdad y nunca se había probado contra este hardware. " +
            "No cierres la app ni desconectes el dispositivo mientras corre — puede tardar varios minutos. " +
            "Esto NO se puede deshacer con un clic (habría que volver a mover cada PIN a mano).",
            "Confirmar renumeración de PINs",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        ApplyButton.IsEnabled = false;
        SelectAllCheckBox.IsEnabled = false;

        var pending = selected.ToList();
        // Ciclos reales (A necesita el PIN de B, B necesita el de A, ninguno está libre desde
        // el inicio) se resuelven "aparcando" a uno del ciclo en un PIN temporal fuera de
        // rango — eso libera su PIN viejo, destraba al resto por el camino normal, y al final
        // se mueve desde el temporal a su destino real (ya libre para entonces). rowByRecord
        // guarda, para cada fila aparcada, el DeviceUserRow ACTUALIZADO (con el PIN temporal)
        // porque row.Row.DeviceUserPin sigue apuntando al PIN viejo que ya no existe en el
        // reloj — usar el original ahí fallaría.
        var parked = new List<(SelectableRepinRow Row, DeviceUserRow CurrentDeviceRow, string RealTarget)>();
        var nextTempPin = 9001;
        var moved = 0;
        var failed = 0;
        var consecutiveFailures = 0;
        var stopped = false;

        while (pending.Count > 0 && !stopped)
        {
            var progressMade = false;

            foreach (var row in pending.ToList())
            {
                // Consulta el estado REAL del reloj en este instante (DevicesViewModel.DeviceUsers
                // se recarga solo después de cada movimiento exitoso) — no una foto vieja de
                // cuando se abrió este diálogo. Mover a alguien libera su PIN viejo, lo que
                // puede destrabar a otra persona en esta misma vuelta.
                var targetOccupied = _viewModel.DeviceUsers.Any(u => u.DeviceUserPin == row.TargetPin);
                if (targetOccupied)
                {
                    continue; // seguimos con el siguiente, quizás se libere en esta misma vuelta
                }

                row.StatusText = "Moviendo huella...";
                var error = await _viewModel.ChangeDeviceUserPinAsync(row.Row, row.TargetPin!);
                if (error is null)
                {
                    row.StatusText = $"✅ Movido a PIN {row.TargetPin}";
                    moved++;
                    consecutiveFailures = 0;
                }
                else
                {
                    row.StatusText = $"❌ {error}";
                    failed++;
                    consecutiveFailures++;
                }

                pending.Remove(row);
                progressMade = true;

                if (consecutiveFailures >= 3)
                {
                    foreach (var rest in pending)
                    {
                        rest.StatusText = "⏸️ Detenido — 3 fallos seguidos, revisa la conexión con el reloj antes de reintentar.";
                    }
                    pending.Clear();
                    stopped = true;
                    break;
                }
            }

            if (stopped)
            {
                break;
            }

            if (!progressMade)
            {
                // Nadie avanzó en toda la vuelta: es un ciclo real. Se rompe moviendo a UNO
                // del grupo a un PIN temporal fuera de rango (libre por construcción) —
                // queda "aparcado" para su movimiento final más abajo, una vez que todo lo
                // demás ya se haya acomodado.
                var breaker = pending[0];
                string tempPin;
                do
                {
                    tempPin = nextTempPin++.ToString();
                }
                while (_viewModel.DeviceUsers.Any(u => u.DeviceUserPin == tempPin));

                breaker.StatusText = $"Moviendo a PIN temporal {tempPin} para romper un ciclo...";
                var error = await _viewModel.ChangeDeviceUserPinAsync(breaker.Row, tempPin);
                pending.Remove(breaker);

                if (error is null)
                {
                    var currentDeviceRow = new DeviceUserRow(
                        new DeviceUserRecord(tempPin, breaker.Row.Name, breaker.Row.PrivilegeLevel, breaker.Row.IsEnabled));
                    parked.Add((breaker, currentDeviceRow, breaker.TargetPin!));
                    breaker.StatusText = $"⏳ Aparcado en PIN temporal {tempPin} — falta moverlo a su destino final ({breaker.TargetPin}).";
                }
                else
                {
                    breaker.StatusText = $"❌ No se pudo aparcar para romper el ciclo: {error}";
                    failed++;
                    consecutiveFailures++;
                    if (consecutiveFailures >= 3)
                    {
                        foreach (var rest in pending)
                        {
                            rest.StatusText = "⏸️ Detenido — 3 fallos seguidos, revisa la conexión con el reloj antes de reintentar.";
                        }
                        pending.Clear();
                        stopped = true;
                    }
                }
            }
        }

        // Segunda pasada: mover a quien quedó aparcado en un PIN temporal a su destino real —
        // para este punto ya debería estar libre, porque todo lo demás ya se movió.
        foreach (var (row, currentDeviceRow, realTarget) in parked)
        {
            row.StatusText = "Moviendo del PIN temporal a su destino final...";
            var error = await _viewModel.ChangeDeviceUserPinAsync(currentDeviceRow, realTarget);
            if (error is null)
            {
                row.StatusText = $"✅ Movido a PIN {realTarget}";
                moved++;
            }
            else
            {
                row.StatusText = $"❌ Quedó en el PIN temporal ({currentDeviceRow.DeviceUserPin}), no se pudo terminar el movimiento a {realTarget}: {error}";
                failed++;
            }
        }

        MessageBox.Show(
            this,
            $"Listo: {moved} PIN(s) movido(s), {failed} con error. Revisa la columna \"Estado\" de cada fila para el detalle.",
            "Renumeración completada", MessageBoxButton.OK, MessageBoxImage.Information);

        ApplyButton.IsEnabled = true;
        SelectAllCheckBox.IsEnabled = true;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
