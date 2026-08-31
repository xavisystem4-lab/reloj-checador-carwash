using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using RelojChecador.Domain.Employees;
using RelojChecador.WPF.Common;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Captura los datos de una marcación manual (empleado, fecha, hora, tipo) — la
/// resolución de a qué dispositivo/PIN atribuirla y el guardado en sí viven en
/// AttendanceViewModel.CreateManualAttendanceAsync, no aquí. Recibe la lista de
/// empleados ya cargada (en vez de consultarla ella misma) para poder mostrar el combo
/// de inmediato al abrir, sin un estado intermedio "cargando…".
///
/// El campo Empleado es un ComboBox editable con autocompletado — pedido explícito del
/// usuario: "que podamos buscar tanto nombre, por apellido, por cualquier letra que
/// vayamos colocando, se vaya autorellenando". <see cref="_selectedEmployee"/> (no
/// EmployeeComboBox.SelectedItem directo) es la fuente de verdad de a quién se eligió,
/// porque re-filtrar ItemsSource mientras se escribe puede dejar SelectedItem en null
/// aunque ya se había elegido a alguien antes de seguir escribiendo.
/// </summary>
public partial class CreateManualAttendanceDialog : Window
{
    private IReadOnlyList<Employee> _allEmployees = [];
    private Employee? _selectedEmployee;
    private bool _suppressTextChanged;

    public Guid? EmployeeId => _selectedEmployee?.Id;
    public int PunchType => EntradaRadioButton.IsChecked == true ? 0 : 1;

    public CreateManualAttendanceDialog(IReadOnlyList<Employee> employees)
    {
        InitializeComponent();
        _allEmployees = employees;
        EmployeeComboBox.ItemsSource = employees;
        if (employees.Count > 0)
        {
            _selectedEmployee = employees[0];
            EmployeeComboBox.SelectedIndex = 0;
        }

        var now = DateTime.Now;
        DatePickerControl.SelectedDate = now.Date;
        TimeTextBox.Text = now.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Filtra por CUALQUIER parte del nombre (no solo el inicio, a diferencia de
    /// IsTextSearchEnabled nativo de WPF) cada vez que se escribe — "por apellido, por
    /// cualquier letra". _suppressTextChanged evita un bucle infinito: seleccionar un
    /// empleado de la lista también dispara TextChanged (el ComboBox actualiza su Text al
    /// DisplayMemberPath del elegido), y eso volvería a filtrar/reabrir el desplegable
    /// justo después de elegir.</summary>
    private void OnEmployeeSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressTextChanged)
        {
            return;
        }

        var term = EmployeeComboBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(term)
            ? _allEmployees
            : _allEmployees.Where(emp => emp.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();

        EmployeeComboBox.ItemsSource = filtered;
        EmployeeComboBox.IsDropDownOpen = filtered.Count > 0;

        // Si lo que quedó escrito ya no corresponde al empleado elegido antes, se
        // considera "sin elegir" hasta que la persona elija algo de la lista otra vez —
        // evita guardar contra un empleado viejo que ya no coincide con lo que se ve en
        // el cuadro de texto.
        if (_selectedEmployee is not null &&
            !string.Equals(_selectedEmployee.FullName, term, StringComparison.OrdinalIgnoreCase))
        {
            _selectedEmployee = null;
        }
    }

    private void OnEmployeeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EmployeeComboBox.SelectedItem is not Employee employee)
        {
            return;
        }

        _selectedEmployee = employee;

        // Al elegir de la lista filtrada, se restaura la lista COMPLETA (no la filtrada)
        // como ItemsSource — si la persona vuelve a abrir el desplegable sin escribir
        // nada más, ve a todos otra vez, no solo a los que coincidían con la búsqueda
        // anterior.
        _suppressTextChanged = true;
        EmployeeComboBox.ItemsSource = _allEmployees;
        EmployeeComboBox.SelectedItem = employee;
        EmployeeComboBox.IsDropDownOpen = false;
        _suppressTextChanged = false;
    }

    /// <summary>Combina la fecha del DatePicker con la hora del TextBox — separados en la
    /// UI (más fácil de capturar/leer a mano) pero un solo DateTime para el ViewModel.
    /// La hora acepta cualquier formato razonable (ver FlexibleTimeParser) — pedido
    /// explícito del usuario: "en hora, utilizar, ya sea, cualquier tipo de formato".</summary>
    public bool TryGetTimestamp(out DateTime timestamp)
    {
        timestamp = default;
        if (DatePickerControl.SelectedDate is not { } date)
        {
            return false;
        }

        if (!FlexibleTimeParser.TryParse(TimeTextBox.Text, out var time))
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
            ErrorTextBlock.Text = "Selecciona un empleado de la lista (escribe para buscar).";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        if (!TryGetTimestamp(out _))
        {
            ErrorTextBlock.Text = "La fecha y la hora son obligatorias (hora en cualquier formato, ej.: 08:00 o 8:00 AM).";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
