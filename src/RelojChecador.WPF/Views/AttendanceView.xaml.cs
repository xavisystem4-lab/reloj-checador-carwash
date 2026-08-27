using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class AttendanceView : UserControl
{
    public AttendanceView()
    {
        InitializeComponent();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadAsync();
    }

    private async void OnCreateManualAttendanceClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel viewModel)
        {
            return;
        }

        var employees = await viewModel.GetActiveEmployeesForManualEntryAsync();
        if (employees.Count == 0)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "No hay empleados activos registrados.",
                "Sin empleados", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new CreateManualAttendanceDialog(employees) { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true || dialog.EmployeeId is not { } employeeId || !dialog.TryGetTimestamp(out var timestamp))
        {
            return;
        }

        var outcome = await viewModel.CreateManualAttendanceAsync(employeeId, timestamp, dialog.PunchType);
        if (!outcome.Success)
        {
            MessageBox.Show(Window.GetWindow(this), outcome.Error, "No se pudo guardar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Exporta exactamente lo que el DataGrid está mostrando ahora (ya con el
    /// filtro de texto aplicado) — el ViewModel arma el texto (BuildCsv), aquí solo se
    /// resuelve dónde guardarlo, que sí es una preocupación de la vista.</summary>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel viewModel)
        {
            return;
        }

        if (viewModel.Attendances.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "No hay marcaciones para exportar con los filtros actuales.",
                "Nada que exportar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var suggestedName = $"asistencias-{viewModel.FromDateText.Replace('/', '-')}-a-{viewModel.ToDateText.Replace('/', '-')}.csv";
        var dialog = new SaveFileDialog
        {
            FileName = suggestedName,
            Filter = "Archivo CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // BOM UTF-8 explícito (igual que el CSV del Dashboard web, dashboard/app.js
            // onExportClick) para que Excel reconozca acentos/eñes sin pedir que el
            // usuario elija la codificación a mano.
            File.WriteAllText(dialog.FileName, viewModel.BuildCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se pudo guardar el archivo: {ex.Message}",
                "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
