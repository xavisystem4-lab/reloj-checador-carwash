using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class PayrollView : UserControl
{
    public PayrollView()
    {
        InitializeComponent();
    }

    /// <summary>Mismo patrón que AttendanceView.OnExportCsvClick: el ViewModel arma el
    /// texto (BuildCsv), aquí solo se resuelve dónde guardarlo.</summary>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PayrollViewModel viewModel)
        {
            return;
        }

        if (viewModel.PayrollRows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "No hay empleados activos para exportar en esta semana.",
                "Nada que exportar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var suggestedName = $"nomina-{viewModel.WeekRangeText.Replace('/', '-').Replace(" ", "")}.csv";
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
            // BOM UTF-8 explícito (mismo criterio que AttendanceView/Dashboard web) para
            // que Excel reconozca acentos/eñes sin pedir la codificación a mano.
            File.WriteAllText(dialog.FileName, viewModel.BuildCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se pudo guardar el archivo: {ex.Message}",
                "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>"📊 Vista previa e imprimir" — pedido explícito del usuario: la misma
    /// previsualización tamaño Carta con el logotipo que ya quedó en el Dashboard web
    /// (Imprimir/Exportar Excel/Exportar PDF). Usa EXACTAMENTE lo que está mostrando la
    /// tabla ahora mismo (viewModel.PayrollRows, ya filtrado por Buscar/Sucursal) — mismo
    /// criterio que el buscador de la previsualización web respeta lo filtrado.</summary>
    private void OnPreviewReportClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PayrollViewModel viewModel)
        {
            return;
        }

        if (viewModel.PayrollRows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "No hay empleados activos para mostrar en esta semana.",
                "Nada que mostrar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new PayrollReportPreviewDialog(
            viewModel.PayrollRows.ToList(), viewModel.WeekRangeText, viewModel.SelectedBranchFilter)
        {
            Owner = Window.GetWindow(this),
        };
        dialog.ShowDialog();
    }

    /// <summary>Mismo patrón que EmployeesView.OnEditMappingsClick: el DataContext del
    /// botón (heredado de la plantilla de celda) ya es el PayrollRow de la fila.</summary>
    private async void OnEditDeductionsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not PayrollViewModel viewModel || (sender as FrameworkElement)?.DataContext is not PayrollRow row)
        {
            return;
        }

        var dialog = new EditPayrollDeductionsDialog(row.EmployeeName, viewModel.WeekRangeText, row.Deductions)
        {
            Owner = Window.GetWindow(this),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = await viewModel.UpdateDeductionsAsync(
            row.Summary.EmployeeId, dialog.IsrAmount, dialog.ImssAmount, dialog.OtherAmount, dialog.OtherLabel, dialog.Notes);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudieron guardar las deducciones", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
