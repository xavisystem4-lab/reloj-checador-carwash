using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using RelojChecador.Application.Devices;
using RelojChecador.Application.Employees;
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
        // "EMP-012" → "12" (ver EmployeeNumberPinRules) — el catálogo vigente usa ese formato
        // y el usuario eligió PIN = solo los dígitos. Nunca para un empleado dado de baja.
        var suggested = row.LinkedEmployeeIsActive ? EmployeeNumberPinRules.ToDevicePin(row.LinkedEmployeeNumber) : null;
        _targetPin = suggested is not null && suggested != row.DeviceUserPin ? suggested : null;
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
        var failed = 0;

        // Dos personas no pueden terminar en el mismo PIN — no se adivina cuál de las dos es.
        foreach (var duplicated in pending.GroupBy(r => r.TargetPin).Where(g => g.Count() > 1).SelectMany(g => g).ToList())
        {
            duplicated.StatusText = $"❌ Otra persona de la lista también va al PIN {duplicated.TargetPin} — corrige uno de los dos.";
            pending.Remove(duplicated);
            failed++;
        }

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
                var occupant = _viewModel.DeviceUsers.FirstOrDefault(u => u.DeviceUserPin == row.TargetPin);
                if (occupant is not null)
                {
                    if (pending.Any(p => p.Row.DeviceUserPin == row.TargetPin))
                    {
                        continue; // lo tiene alguien de la lista que todavía se va a mover — quizás en esta misma vuelta
                    }

                    // Lo ocupa alguien que NO se va a mover: esperar no lo libera nunca. Antes esto
                    // caía en "ciclo" y la persona se quedaba para siempre en un PIN temporal 9001+.
                    row.StatusText = $"❌ El PIN {row.TargetPin} lo tiene \"{occupant.Name}\" en el reloj y no está en esta lista. " +
                                     "Muévelo o bórralo desde Usuarios del reloj y vuelve a intentar.";
                    failed++;
                    pending.Remove(row);
                    progressMade = true;
                    continue;
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
                // Su destino no se liberó (quien lo tenía falló al moverse): se regresa a su PIN
                // original, que sí quedó libre al aparcarla, en vez de dejarla en el temporal.
                var backError = await _viewModel.ChangeDeviceUserPinAsync(currentDeviceRow, row.Row.DeviceUserPin);
                row.StatusText = backError is null
                    ? $"❌ No se pudo mover a {realTarget} ({error}); se regresó a su PIN original {row.Row.DeviceUserPin}."
                    : $"❌ Quedó en el PIN temporal ({currentDeviceRow.DeviceUserPin}), no se pudo terminar el movimiento a {realTarget}: {error}";
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
