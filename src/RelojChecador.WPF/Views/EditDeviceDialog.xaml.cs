using System.Linq;
using System.Windows;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para editar los datos de un dispositivo ya registrado — mismos campos que
/// AddDeviceDialog, precargados con los valores actuales. Permite reasignar la sucursal
/// (ver Device.UpdateDetails) — a diferencia de Employee.Number/Branch.Code, BranchId no
/// es una clave de negocio del dispositivo, así que no hace falta ningún tratamiento
/// especial para "corregirla".
/// </summary>
public partial class EditDeviceDialog : Window
{
    public Guid DeviceId { get; }
    public string DeviceName => NameTextBox.Text.Trim();
    public string Brand => BrandTextBox.Text.Trim();
    public string Model => ModelTextBox.Text.Trim();
    public string IpAddress => IpTextBox.Text.Trim();
    public int TcpPort { get; private set; }
    public string? SerialNumber => string.IsNullOrWhiteSpace(SerialTextBox.Text) ? null : SerialTextBox.Text.Trim();
    public string? MacAddress => string.IsNullOrWhiteSpace(MacTextBox.Text) ? null : MacTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    // Vacío = "no cambiar la clave ya guardada" — nunca se precarga la clave existente
    // aquí (no se muestra un secreto ya guardado de vuelta en la pantalla).
    public string? CommunicationKey => string.IsNullOrEmpty(CommunicationKeyPasswordBox.Password) ? null : CommunicationKeyPasswordBox.Password;

    public EditDeviceDialog(Device device, IReadOnlyList<Branch> branches)
    {
        InitializeComponent();
        DeviceId = device.Id;

        BranchComboBox.ItemsSource = branches;
        var currentBranch = branches.FirstOrDefault(b => b.Id == device.BranchId);
        BranchComboBox.SelectedItem = currentBranch ?? branches.FirstOrDefault();

        NameTextBox.Text = device.Name;
        BrandTextBox.Text = device.Brand;
        ModelTextBox.Text = device.Model;
        IpTextBox.Text = device.IpAddress;
        PortTextBox.Text = device.TcpPort.ToString();
        SerialTextBox.Text = device.SerialNumber;
        MacTextBox.Text = device.MacAddress;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DeviceName) || string.IsNullOrWhiteSpace(Brand) ||
            string.IsNullOrWhiteSpace(Model) || string.IsNullOrWhiteSpace(IpAddress) ||
            SelectedBranch is null)
        {
            ShowError("Nombre, sucursal, marca, modelo e IP son obligatorios.");
            return;
        }

        if (!int.TryParse(PortTextBox.Text.Trim(), out var port) || port is <= 0 or > 65535)
        {
            ShowError("El puerto TCP debe ser un número entre 1 y 65535.");
            return;
        }

        TcpPort = port;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
