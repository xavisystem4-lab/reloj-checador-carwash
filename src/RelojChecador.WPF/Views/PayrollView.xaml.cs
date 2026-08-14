using System.IO;
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
}
