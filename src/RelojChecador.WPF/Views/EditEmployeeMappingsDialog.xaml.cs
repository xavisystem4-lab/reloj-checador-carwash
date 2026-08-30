using System.Collections.ObjectModel;
using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para corregir el PIN de uno o más vínculos ya existentes de un empleado — caso
/// real: el usuario capturó el número de empleado en vez del PIN real del reloj al
/// vincular, y "vincular de nuevo" con el PIN correcto lo rechazaba (índice único
/// (DeviceId, EmployeeId), ver EmployeeDeviceMappingConfiguration). También deja QUITAR un
/// vínculo puntual (🗑 por fila) — pedido explícito del usuario tras encontrar a alguien
/// vinculado de más a un reloj de prueba deshabilitado, además del real. No permite agregar
/// vínculos nuevos aquí — eso sigue siendo "Vincular a dispositivo".
/// </summary>
public partial class EditEmployeeMappingsDialog : Window
{
    /// <summary>Fila editable del ItemsControl — POCO simple, sin INotifyPropertyChanged:
    /// nada más depende de NewPin cambiando en vivo, solo se lee su valor final al
    /// guardar.</summary>
    public sealed class EditableMappingRow(Guid mappingId, string deviceName, string originalPin)
    {
        public Guid MappingId { get; } = mappingId;
        public string DeviceName { get; } = deviceName;
        public string OriginalPin { get; } = originalPin;
        public string NewPin { get; set; } = originalPin;
    }

    private readonly ObservableCollection<EditableMappingRow> _rows;
    private readonly List<Guid> _removedMappingIds = [];

    /// <summary>Solo los vínculos cuyo PIN realmente cambió respecto al original.</summary>
    public IReadOnlyDictionary<Guid, string> ChangedPins { get; private set; } = new Dictionary<Guid, string>();

    /// <summary>Vínculos marcados con 🗑 — se quitan del ItemsControl al instante, pero el
    /// borrado real en la base pasa hasta que quien llama a este diálogo confirma con
    /// "Guardar" (ver EmployeesView.xaml.cs, OnEditMappingsClick).</summary>
    public IReadOnlyList<Guid> RemovedMappingIds => _removedMappingIds;

    public EditEmployeeMappingsDialog(string employeeFullName, IReadOnlyList<EmployeeMappingInfo> mappings)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Editar vínculos de {employeeFullName}";

        _rows = [.. mappings.Select(m => new EditableMappingRow(m.MappingId, m.DeviceName, m.DeviceUserPin))];
        MappingsItemsControl.ItemsSource = _rows;
    }

    private void OnRemoveMappingClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not EditableMappingRow row)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            $"¿Quitar el vínculo con \"{row.DeviceName}\" (PIN {row.OriginalPin})?\n\n" +
            "Las marcaciones ya guardadas con ese PIN no se tocan — solo deja de reconocerse a partir de ahora.",
            "Confirmar",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        _removedMappingIds.Add(row.MappingId);
        _rows.Remove(row);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        var emptyPin = _rows.FirstOrDefault(r => string.IsNullOrWhiteSpace(r.NewPin));
        if (emptyPin is not null)
        {
            ShowError($"El PIN de \"{emptyPin.DeviceName}\" no puede quedar vacío.");
            return;
        }

        ChangedPins = _rows
            .Where(r => r.NewPin.Trim() != r.OriginalPin)
            .ToDictionary(r => r.MappingId, r => r.NewPin.Trim());

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
