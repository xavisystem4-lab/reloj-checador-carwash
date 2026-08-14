using System.Windows;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para corregir el PIN de uno o más vínculos ya existentes de un empleado — caso
/// real: el usuario capturó el número de empleado en vez del PIN real del reloj al
/// vincular, y "vincular de nuevo" con el PIN correcto lo rechazaba (índice único
/// (DeviceId, EmployeeId), ver EmployeeDeviceMappingConfiguration). No permite eliminar
/// vínculos ni agregar nuevos aquí — eso sigue siendo "Vincular a dispositivo"; este
/// diálogo solo corrige el PIN de los que ya existen.
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

    private readonly List<EditableMappingRow> _rows;

    /// <summary>Solo los vínculos cuyo PIN realmente cambió respecto al original.</summary>
    public IReadOnlyDictionary<Guid, string> ChangedPins { get; private set; } = new Dictionary<Guid, string>();

    public EditEmployeeMappingsDialog(string employeeFullName, IReadOnlyList<EmployeeMappingInfo> mappings)
    {
        InitializeComponent();
        HeaderTextBlock.Text = $"Editar vínculos de {employeeFullName}";

        _rows = mappings.Select(m => new EditableMappingRow(m.MappingId, m.DeviceName, m.DeviceUserPin)).ToList();
        MappingsItemsControl.ItemsSource = _rows;
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
