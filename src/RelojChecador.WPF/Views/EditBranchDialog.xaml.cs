using System.Windows;
using RelojChecador.Domain.Branches;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para editar los datos de una sucursal ya existente — mismos campos que
/// AddBranchDialog (el código sí es editable, ver Branch.ChangeCode: corrige un error de
/// captura del alta, igual que Employee.Number en EditEmployeeDialog) más el estatus
/// Activa/Inactiva, que AddBranchDialog no necesita porque una sucursal nueva siempre
/// nace activa.
/// </summary>
public partial class EditBranchDialog : Window
{
    public Guid BranchId { get; }
    public string Code => CodeTextBox.Text.Trim();
    public string BranchName => NameTextBox.Text.Trim();
    public string TimeZoneId => TimeZoneTextBox.Text.Trim();
    public string? LegalEntityName => string.IsNullOrWhiteSpace(LegalEntityNameTextBox.Text) ? null : LegalEntityNameTextBox.Text.Trim();
    public string? Address => string.IsNullOrWhiteSpace(AddressTextBox.Text) ? null : AddressTextBox.Text.Trim();
    // "IsBranchActive", no "IsActive": Window ya tiene una propiedad IsActive propia (si
    // la ventana tiene el foco) — nombrarla igual la ocultaría en silencio (warning CS0108).
    public bool IsBranchActive => IsActiveCheckBox.IsChecked == true;

    public EditBranchDialog(Branch branch)
    {
        InitializeComponent();
        BranchId = branch.Id;
        CodeTextBox.Text = branch.Code;
        NameTextBox.Text = branch.Name;
        TimeZoneTextBox.Text = branch.TimeZoneId;
        LegalEntityNameTextBox.Text = branch.LegalEntityName;
        AddressTextBox.Text = branch.Address;
        IsActiveCheckBox.IsChecked = branch.IsActive;
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
