using System.Globalization;
using System.Windows;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Captura los datos de una marcación manual (empleado, fecha, hora, tipo) — la
/// resolución de a qué dispositivo/PIN atribuirla y el guardado en sí viven en
/// AttendanceViewModel.CreateManualAttendanceAsync, no aquí. Recibe la lista de
/// empleados ya cargada (en vez de consultarla ella misma) para poder mostrar el combo
/// de inmediato al abrir, sin un estado intermedio "cargando…".
/// </summary>
public partial class CreateManualAttendanceDialog : Window
{
    private const string TimeFormat = "HH:mm";

    public Guid? EmployeeId => (EmployeeComboBox.SelectedItem as Employee)?.Id;
    public int PunchType => EntradaRadioButton.IsChecked == true ? 0 : 1;

    public CreateManualAttendanceDialog(IReadOnlyList<Employee> employees)
    {
        InitializeComponent();
        EmployeeComboBox.ItemsSource = employees;
        if (employees.Count > 0)
        {
            EmployeeComboBox.SelectedIndex = 0;
        }

        var now = DateTime.Now;
        DatePickerControl.SelectedDate = now.Date;
        TimeTextBox.Text = now.ToString(TimeFormat, CultureInfo.InvariantCulture);
    }

    /// <summary>Combina la fecha del DatePicker con la hora del TextBox — separados en la
    /// UI (más fácil de capturar/leer a mano) pero un solo DateTime para el ViewModel.</summary>
    public bool TryGetTimestamp(out DateTime timestamp)
    {
        timestamp = default;
        if (DatePickerControl.SelectedDate is not { } date)
        {
            return false;
        }

        if (!TimeSpan.TryParseExact(TimeTextBox.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time))
        {
            return false;
        }

        timestamp = date.Date.Add(time);
        return true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (EmployeeId is null)
        {
            ErrorTextBlock.Text = "Selecciona un empleado.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!TryGetTimestamp(out _))
        {
            ErrorTextBlock.Text = "La fecha y la hora (formato HH:mm, 24 horas) son obligatorias.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
