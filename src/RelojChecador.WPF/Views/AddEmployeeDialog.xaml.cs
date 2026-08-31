using System.Globalization;
using System.Windows;
using RelojChecador.Domain.Branches;
using RelojChecador.Domain.Devices;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo para capturar los datos de un nuevo empleado, con vínculo a dispositivo
/// opcional en el mismo formulario (checkbox "Vincular a un reloj checador ahora") — evita
/// el paso aparte de "Vincular a dispositivo" en la lista de Empleados cuando el alta y el
/// PIN se conocen al mismo tiempo. Sueldo semanal y tarifa de hora extra son insumo de
/// nómina sin ningún cálculo fiscal — ver Employee.cs y WorkedHoursCalculator. La
/// validación real de negocio (número/nombre requeridos, longitud máxima, duplicados, PIN
/// duplicado en el dispositivo) la hace el dominio/base al guardar; aquí solo se evita
/// mandar campos obviamente vacíos, negativos o una fecha ilegible, para no abrir un viaje
/// a la base de datos innecesario.
/// </summary>
public partial class AddEmployeeDialog : Window
{
    private const string HireDateFormat = "dd/MM/yyyy";
    private const string ScheduleTimeFormat = "HH:mm";

    public string Number => NumberTextBox.Text.Trim();
    public string FullName => FullNameTextBox.Text.Trim();
    public Branch? SelectedBranch => BranchComboBox.SelectedItem as Branch;
    public DateOnly HireDate { get; private set; }
    public string? Department => string.IsNullOrWhiteSpace(DepartmentTextBox.Text) ? null : DepartmentTextBox.Text.Trim();
    public string? Position => string.IsNullOrWhiteSpace(PositionTextBox.Text) ? null : PositionTextBox.Text.Trim();
    public decimal? WeeklySalary { get; private set; }
    public decimal? OvertimeHourlyRate { get; private set; }
    public string? Notes => string.IsNullOrWhiteSpace(NotesTextBox.Text) ? null : NotesTextBox.Text.Trim();
    public TimeOnly? ScheduledStartTime { get; private set; }
    public TimeOnly? ScheduledEndTime { get; private set; }

    /// <summary>Null si el checkbox "Vincular a un reloj checador ahora" no está marcado —
    /// vincular al dar de alta es opcional.</summary>
    public Device? SelectedDevice => LinkDeviceCheckBox.IsChecked == true ? DeviceComboBox.SelectedItem as Device : null;
    public string? DeviceUserPin => LinkDeviceCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(DevicePinTextBox.Text)
        ? DevicePinTextBox.Text.Trim()
        : null;

    public AddEmployeeDialog(IReadOnlyList<Branch> branches, IReadOnlyList<Device> devices)
    {
        InitializeComponent();
        BranchComboBox.ItemsSource = branches;
        if (branches.Count > 0)
        {
            BranchComboBox.SelectedIndex = 0;
        }

        DeviceComboBox.ItemsSource = devices;
        if (devices.Count > 0)
        {
            DeviceComboBox.SelectedIndex = 0;
        }

        HireDateTextBox.Text = DateTime.Now.ToString(HireDateFormat, CultureInfo.InvariantCulture);
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

        if (!DateOnly.TryParseExact(
                HireDateTextBox.Text.Trim(), HireDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var hireDate))
        {
            ShowError("La fecha de alta debe tener el formato dd/mm/aaaa.");
            return;
        }

        // Vacío = sueldo pendiente de captura (null) — nunca se asume $0 en su lugar.
        decimal? weeklySalary = null;
        if (!string.IsNullOrWhiteSpace(WeeklySalaryTextBox.Text))
        {
            if (!decimal.TryParse(WeeklySalaryTextBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var salary)
                || salary < 0)
            {
                ShowError("El sueldo semanal debe ser un número mayor o igual a 0 (o déjalo vacío si aún no se sabe).");
                return;
            }
            weeklySalary = salary;
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

        if (LinkDeviceCheckBox.IsChecked == true && (SelectedDevice is null || string.IsNullOrWhiteSpace(DeviceUserPin)))
        {
            ShowError("Para vincular a un dispositivo ahora, elige el reloj y escribe el PIN.");
            return;
        }

        // Ambos vacíos = sin capturar (válido); uno solo capturado no es útil para
        // reportar y no lo acepta el dominio (ver Employee.UpdateSchedule).
        TimeOnly? scheduledStartTime = null;
        if (!string.IsNullOrWhiteSpace(ScheduledStartTimeTextBox.Text))
        {
            if (!TimeOnly.TryParseExact(ScheduledStartTimeTextBox.Text.Trim(), ScheduleTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart))
            {
                ShowError("La hora de entrada debe tener el formato HH:mm (ej.: 08:00).");
                return;
            }
            scheduledStartTime = parsedStart;
        }

        TimeOnly? scheduledEndTime = null;
        if (!string.IsNullOrWhiteSpace(ScheduledEndTimeTextBox.Text))
        {
            if (!TimeOnly.TryParseExact(ScheduledEndTimeTextBox.Text.Trim(), ScheduleTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedEnd))
            {
                ShowError("La hora de salida debe tener el formato HH:mm (ej.: 16:00).");
                return;
            }
            scheduledEndTime = parsedEnd;
        }

        if ((scheduledStartTime is null) != (scheduledEndTime is null))
        {
            ShowError("Captura ambas horas del horario (entrada y salida), o déjalas las dos vacías.");
            return;
        }

        HireDate = hireDate;
        WeeklySalary = weeklySalary;
        OvertimeHourlyRate = overtimeHourlyRate;
        ScheduledStartTime = scheduledStartTime;
        ScheduledEndTime = scheduledEndTime;
        DialogResult = true;
    }

    private void ShowError(string message)
    {
        ErrorTextBlock.Text = message;
        ErrorTextBlock.Visibility = Visibility.Visible;
    }
}
