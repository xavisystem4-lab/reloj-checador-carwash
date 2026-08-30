using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>Envuelve un <see cref="DeviceUserRow"/> con el PIN destino calculado (el Número
/// del empleado vinculado) y el estado de la operación — mismo patrón de checkbox que los
/// demás diálogos de selección masiva, más una columna de progreso que se va llenando
/// mientras corre (mover una huella no es instantáneo).</summary>
public sealed partial class SelectableRepinRow(DeviceUserRow row) : ObservableObject
{
    public DeviceUserRow Row { get; } = row;

    /// <summary>El Número del empleado vinculado, solo si es un PIN válido (dígitos) y
    /// distinto del PIN actual — si no, no hay nada que mover para esta fila.</summary>
    public string? TargetPin { get; } =
        !string.IsNullOrWhiteSpace(row.LinkedEmployeeNumber)
        && row.LinkedEmployeeNumber.All(char.IsDigit)
        && row.LinkedEmployeeNumber != row.DeviceUserPin
            ? row.LinkedEmployeeNumber
            : null;

    public bool HasValidTarget => TargetPin is not null;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string _statusText = "";
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
            row.StatusText = row.HasValidTarget ? "Pendiente" : "Sin folio numérico válido o ya coincide — no se puede mover";
            row.PropertyChanged += (_, _) => UpdateSelectionState();
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
        var moved = 0;
        var failed = 0;
        var consecutiveFailures = 0;

        while (pending.Count > 0)
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
                    break;
                }
            }

            if (!progressMade)
            {
                // Nadie avanzó en toda la vuelta: lo que queda es un ciclo real (A necesita
                // el PIN de B y viceversa) — se reporta en vez de forzar un movimiento doble.
                foreach (var stuck in pending)
                {
                    stuck.StatusText = "⏸️ Ciclo de PIN — no se pudo mover automáticamente, hazlo a mano en el orden correcto.";
                }
                break;
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
