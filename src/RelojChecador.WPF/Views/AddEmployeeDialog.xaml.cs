using System.Globalization;
using System.Windows;
using RelojChecador.Domain.Branches;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para capturar los datos de un nuevo empleado. La validación real de
/// negocio (número/nombre requeridos, longitud máxima, duplicados) la hace el dominio
/// (Employee.Create + índice único en la base) cuando el llamador intenta guardar; aquí
/// solo se evita mandar campos obviamente vacíos o una fecha ilegible, para no abrir un
/// viaje a la base de datos innecesario.
/// </summary>
public partial class AddEmployeeDialog : Window
{
    private const string HireDateFormat = "dd/MM/yyyy";

    public string Number => NumberTextBox.Text.Trim();
    public string FullName => FullNameTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    public DateOnly HireDate { get; private set; }
    public string? Department => string.IsNullOrWhiteSpace(DepartmentTextBox.Text) ? null : DepartmentTextBox.Text.Trim();
    public string? Position => string.IsNullOrWhiteSpace(PositionTextBox.Text) ? null : PositionTextBox.Text.Trim();

    public AddEmployeeDialog(IReadOnlyList<Branch> branches)
    {
        InitializeComponent();
        BranchComboBox.ItemsSource = branches;
        if (branches.Count > 0)
        {
            BranchComboBox.SelectedIndex = 0;
        }

        HireDateTextBox.Text = DateTime.Now.ToString(HireDateFormat, CultureInfo.InvariantCulture);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Number) || string.IsNullOrWhiteSpace(FullName) || SelectedBranch is null)
        {
            ShowError("Número, nombre y sucursal son obligatorios.");
            return;
        }

        if (!DateOnly.TryParseExact(
                HireDateTextBox.Text.Trim(), HireDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var hireDate))
        {
            ShowError("La fecha de alta debe tener el formato dd/mm/aaaa.");
            return;
        }

        HireDate = hireDate;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
