using System.Globalization;
using System.Linq;
using System.Windows;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para editar los datos de un empleado ya existente, incluido su número (se
/// puede corregir un error de captura del alta — ver Employee.ChangeNumber) y, si todavía
/// no tiene ningún dispositivo vinculado, el primer vínculo (dispositivo + PIN) en el
/// mismo formulario. Si ya tiene uno o más vínculos, esa sección no se muestra — agregar
/// otro dispositivo se sigue haciendo con "Vincular a dispositivo" en la lista, para no
/// meter aquí la complejidad de editar/quitar vínculos existentes.
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

    public string Number => NumberTextBox.Text.Trim();
    public string FullName => FullNameTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    public string? Department => string.IsNullOrWhiteSpace(DepartmentTextBox.Text) ? null : DepartmentTextBox.Text.Trim();
    public string? Position => string.IsNullOrWhiteSpace(PositionTextBox.Text) ? null : PositionTextBox.Text.Trim();
    public string? Phone => string.IsNullOrWhiteSpace(PhoneTextBox.Text) ? null : PhoneTextBox.Text.Trim();
    public string? Email => string.IsNullOrWhiteSpace(EmailTextBox.Text) ? null : EmailTextBox.Text.Trim();
    public EmploymentStatus SelectedStatus => (StatusComboBox.SelectedItem as StatusOption)?.Value ?? EmploymentStatus.Active;
    public decimal WeeklySalary { get; private set; }
    public decimal? OvertimeHourlyRate { get; private set; }

    /// <summary>Null si la sección de vínculo no aplica (ya tenía uno) o el checkbox no
    /// está marcado.</summary>
    public Device? SelectedDevice => LinkDeviceSection.Visibility == Visibility.Visible && LinkDeviceCheckBox.IsChecked == true
        ? DeviceComboBox.SelectedItem as Device
        : null;
    public string? DeviceUserPin =>
        SelectedDevice is not null && !string.IsNullOrWhiteSpace(DevicePinTextBox.Text)
            ? DevicePinTextBox.Text.Trim()
            : null;

    public EditEmployeeDialog(EmployeeRow row, IReadOnlyList<Branch> branches, IReadOnlyList<Device> devices)
    {
        InitializeComponent();

        var employee = row.Employee;
        NumberTextBox.Text = employee.Number.Value;
        FullNameTextBox.Text = employee.FullName;
        DepartmentTextBox.Text = employee.Department ?? "";
        PositionTextBox.Text = employee.Position ?? "";
        PhoneTextBox.Text = employee.Phone ?? "";
        EmailTextBox.Text = employee.Email ?? "";
        WeeklySalaryTextBox.Text = employee.WeeklySalary.ToString("0.##", CultureInfo.InvariantCulture);
        OvertimeHourlyRateTextBox.Text = employee.OvertimeHourlyRate?.ToString("0.##", CultureInfo.InvariantCulture) ?? "";

        BranchComboBox.ItemsSource = branches;
        BranchComboBox.SelectedItem = branches.FirstOrDefault(b => b.Id == employee.BranchId);

        StatusComboBox.ItemsSource = StatusOptions;
        StatusComboBox.SelectedItem = StatusOptions.First(o => o.Value == employee.Status);

        var alreadyLinked = row.LinkedDevicesSummary != "Sin vincular";
        LinkDeviceSection.Visibility = alreadyLinked ? Visibility.Collapsed : Visibility.Visible;
        if (!alreadyLinked)
        {
            DeviceComboBox.ItemsSource = devices;
            if (devices.Count > 0)
            {
                DeviceComboBox.SelectedIndex = 0;
            }
        }
    }

    private void OnLinkDeviceCheckedChanged(object sender, RoutedEventArgs e)
    {
        LinkDevicePanel.Visibility = LinkDeviceCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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

        if (!decimal.TryParse(WeeklySalaryTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var weeklySalary)
            || weeklySalary < 0)
        {
            ShowError("El sueldo semanal debe ser un número mayor o igual a 0.");
            return;
        }

        decimal? overtimeHourlyRate = null;
        if (!string.IsNullOrWhiteSpace(OvertimeHourlyRateTextBox.Text))
        {
            if (!decimal.TryParse(OvertimeHourlyRateTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)
                || rate < 0)
            {
                ShowError("La tarifa de hora extra debe ser un número mayor o igual a 0 (o déjala vacía si no aplica).");
                return;
            }
            overtimeHourlyRate = rate;
        }

        if (LinkDeviceSection.Visibility == Visibility.Visible && LinkDeviceCheckBox.IsChecked == true
            && (DeviceComboBox.SelectedItem is not Device || string.IsNullOrWhiteSpace(DevicePinTextBox.Text)))
        {
            ShowError("Para vincular a un dispositivo ahora, elige el reloj y escribe el PIN.");
            return;
        }

        WeeklySalary = weeklySalary;
        OvertimeHourlyRate = overtimeHourlyRate;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
