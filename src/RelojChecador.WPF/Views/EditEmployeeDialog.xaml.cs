using System.Linq;
using System.Windows;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Employees;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para editar los datos de un empleado ya existente. Número y fecha de alta se
/// muestran de solo lectura (ver comentario en el XAML); el resto de campos precarga los
/// valores actuales. La validación real de negocio (nombre requerido, etc.) la hace el
/// dominio al guardar (ver EmployeesViewModel.UpdateEmployeeAsync) — aquí solo se evita
/// mandar el nombre obviamente vacío.
/// </summary>
public partial class EditEmployeeDialog : Window
{
    private sealed record StatusOption(EmploymentStatus Value, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly StatusOption[] StatusOptions =
    [
        new(EmploymentStatus.Active, "Activo"),
        new(EmploymentStatus.OnLeave, "De permiso"),
        new(EmploymentStatus.Inactive, "Inactivo"),
        new(EmploymentStatus.Terminated, "Baja"),
    ];

    public string FullName => FullNameTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    public string? Department => string.IsNullOrWhiteSpace(DepartmentTextBox.Text) ? null : DepartmentTextBox.Text.Trim();
    public string? Position => string.IsNullOrWhiteSpace(PositionTextBox.Text) ? null : PositionTextBox.Text.Trim();
    public string? Phone => string.IsNullOrWhiteSpace(PhoneTextBox.Text) ? null : PhoneTextBox.Text.Trim();
    public string? Email => string.IsNullOrWhiteSpace(EmailTextBox.Text) ? null : EmailTextBox.Text.Trim();
    public EmploymentStatus SelectedStatus => (StatusComboBox.SelectedItem as StatusOption)?.Value ?? EmploymentStatus.Active;

    public EditEmployeeDialog(Employee employee, IReadOnlyList<Branch> branches)
    {
        InitializeComponent();

        NumberTextBlock.Text = employee.Number.Value;
        FullNameTextBox.Text = employee.FullName;
        DepartmentTextBox.Text = employee.Department ?? "";
        PositionTextBox.Text = employee.Position ?? "";
        PhoneTextBox.Text = employee.Phone ?? "";
        EmailTextBox.Text = employee.Email ?? "";

        BranchComboBox.ItemsSource = branches;
        BranchComboBox.SelectedItem = branches.FirstOrDefault(b => b.Id == employee.BranchId);

        StatusComboBox.ItemsSource = StatusOptions;
        StatusComboBox.SelectedItem = StatusOptions.First(o => o.Value == employee.Status);
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FullName) || SelectedBranch is null)
        {
            ShowError("Nombre y sucursal son obligatorios.");
            return;
        }

        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
