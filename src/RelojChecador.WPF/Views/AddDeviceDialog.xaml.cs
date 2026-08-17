using System.Windows;
using RelojChecador.Domain.Branches;

namespace RelojChecador.WPF.Views;

public partial class AddDeviceDialog : Window
{
    public string DeviceName => NameTextBox.Text.Trim();
    public string Brand => BrandTextBox.Text.Trim();
    public string Model => ModelTextBox.Text.Trim();
    public string IpAddress => IpTextBox.Text.Trim();
    public int TcpPort { get; private set; }
    public string? SerialNumber => string.IsNullOrWhiteSpace(SerialTextBox.Text) ? null : SerialTextBox.Text.Trim();
    public string? MacAddress => string.IsNullOrWhiteSpace(MacTextBox.Text) ? null : MacTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    public string? CommunicationKey => string.IsNullOrEmpty(CommunicationKeyPasswordBox.Password) ? null : CommunicationKeyPasswordBox.Password;

    public AddDeviceDialog(IReadOnlyList<Branch> branches)
    {
        InitializeComponent();
        BranchComboBox.ItemsSource = branches;
        if (branches.Count > 0)
        {
            BranchComboBox.SelectedIndex = 0;
        }
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
