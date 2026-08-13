using System.Windows;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para capturar los datos de una nueva sucursal. La validación real de
/// negocio (código/nombre/zona horaria requeridos, formato, duplicados) la hace el
/// dominio (Branch.Create + índice único en la base) cuando el llamador intenta guardar;
/// aquí solo se evita mandar campos obviamente vacíos, para no abrir un viaje a la base
/// de datos innecesario.
/// </summary>
public partial class AddBranchDialog : Window
{
    public string Code => CodeTextBox.Text.Trim();
    public string BranchName => NameTextBox.Text.Trim();
    public string TimeZoneId => TimeZoneTextBox.Text.Trim();
    public string? LegalEntityName => string.IsNullOrWhiteSpace(LegalEntityNameTextBox.Text) ? null : LegalEntityNameTextBox.Text.Trim();
    public string? Address => string.IsNullOrWhiteSpace(AddressTextBox.Text) ? null : AddressTextBox.Text.Trim();

    public AddBranchDialog()
    {
        InitializeComponent();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Code) || string.IsNullOrWhiteSpace(BranchName) || string.IsNullOrWhiteSpace(TimeZoneId))
        {
            ErrorTextBlock.Text = "Código, nombre y zona horaria son obligatorios.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
