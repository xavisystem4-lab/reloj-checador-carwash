using System.Windows;
using RelojChecador.WPF.Services;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ThemeService _themeService;

    public MainWindow(
        MainViewModel branchesViewModel, EmployeesViewModel employeesViewModel, DevicesViewModel devicesViewModel,
        AttendanceViewModel attendanceViewModel, PayrollViewModel payrollViewModel, UpdateViewModel updateViewModel,
        ThemeService themeService)
    {
        InitializeComponent();

        _themeService = themeService;

        DataContext = updateViewModel;
        BranchesViewControl.DataContext = branchesViewModel;
        EmployeesViewControl.DataContext = employeesViewModel;
        DevicesViewControl.DataContext = devicesViewModel;
        AttendanceViewControl.DataContext = attendanceViewModel;
        PayrollViewControl.DataContext = payrollViewModel;

        // "📤 Enviar empleados al reloj" vive en Empleados pero reutiliza la MISMA
        // DevicesViewModel de la pestaña Dispositivos (conectar/enviar/desconectar) — no
        // pasa por DataContext porque el DataContext de EmployeesViewControl ya es
        // EmployeesViewModel, así que se asigna aparte (ver EmployeesView.xaml.cs).
        EmployeesViewControl.DevicesViewModel = devicesViewModel;

        // ThemeService.Initialize() ya corrió en App.xaml.cs (antes de crear esta ventana);
        // aquí solo se refleja ese estado ya aplicado en el ícono del botón.
        UpdateDarkModeButtonContent();

        Loaded += async (_, _) =>
        {
            await branchesViewModel.InitializeAsync();
            await employeesViewModel.InitializeAsync();
            await devicesViewModel.InitializeAsync();
            await attendanceViewModel.InitializeAsync();
            await payrollViewModel.InitializeAsync();
        };
    }

    private void OnToggleDarkModeClick(object sender, RoutedEventArgs e)
    {
        _themeService.Toggle();
        UpdateDarkModeButtonContent();
    }

    /// <summary>El ícono muestra hacia dónde se va a cambiar (no el estado actual) —
    /// convención común de este tipo de interruptor: en modo claro se ve la luna (pasar a
    /// oscuro), en modo oscuro se ve el sol (pasar a claro).</summary>
    private void UpdateDarkModeButtonContent() =>
        DarkModeToggleButton.Content = _themeService.IsDarkMode ? "☀️" : "🌙";
}
