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

        var suggestedName = $"asistencias-{viewModel.FromDateText:yyyy-MM-dd}-a-{viewModel.ToDateText:yyyy-MM-dd}.csv";
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

    /// <summary>Corrección de datos de una sola vez — pedido explícito del usuario: pone
    /// TODAS las marcaciones existentes en "Entrada", incluidas las que ya tenían otro tipo.
    /// De aquí en adelante las marcaciones NUEVAS se siguen clasificando solas según la
    /// regla de las 7h50 (ver DevicesViewModel.PersistAttendanceAsync); esto solo corrige el
    /// historial que ya estaba cargado antes de que esa regla existiera.</summary>
    private async void OnNormalizePunchTypesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AttendanceViewModel viewModel)
        {
            return;
        }

        var confirmed = MessageBox.Show(
            Window.GetWindow(this),
            "¿Marcar TODAS las marcaciones de asistencia (de cualquier empleado, cualquier fecha) como \"Entrada\"?\n\n" +
            "Esto incluye marcaciones que ya tenían otro tipo asignado. Es una corrección de datos de una sola vez — " +
            "las marcaciones nuevas que lleguen después se seguirán clasificando solas según la regla de las 7h50. " +
            "Esto NO se puede deshacer.",
            "Confirmar normalización",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        var affected = await viewModel.NormalizePunchTypesAsync();
        MessageBox.Show(
            Window.GetWindow(this),
            $"Se actualizaron {affected} marcación(es) a \"Entrada\".",
            "Normalización completa", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
