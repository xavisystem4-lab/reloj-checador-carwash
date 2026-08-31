using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Microsoft.Win32;
using RelojChecador.WPF.Services;
using RelojChecador.WPF.ViewModels;
using Serilog;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Vista previa tamaño Carta apaisada del reporte de nómina, con Imprimir/Exportar Excel/
/// Exportar PDF — pedido explícito del usuario: "¿Quieres que le agregue a Reportes ... la
/// misma previsualización tipo hoja con el logo, más los botones Imprimir/Exportar Excel/
/// Exportar PDF, igual que ya quedó en la web?" (respondido "Sí"). El documento (ver
/// PayrollReportDocumentBuilder) se construye UNA sola vez en el constructor y se reutiliza
/// tanto para la vista en pantalla como para Imprimir/Exportar PDF — así el PDF sale
/// idéntico a lo impreso.
/// </summary>
public partial class PayrollReportPreviewDialog : Window
{
    private readonly IReadOnlyList<PayrollRow> _rows;
    private readonly string _weekRangeText;
    private readonly string _branchFilterText;
    private readonly FlowDocument _document;

    public PayrollReportPreviewDialog(IReadOnlyList<PayrollRow> rows, string weekRangeText, string branchFilterText)
    {
        InitializeComponent();
        _rows = rows;
        _weekRangeText = weekRangeText;
        _branchFilterText = branchFilterText;
        _document = PayrollReportDocumentBuilder.Build(rows, weekRangeText, branchFilterText);
        DocumentViewer.Document = _document;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var paginator = ((IDocumentPaginatorSource)_document).DocumentPaginator;
            paginator.PageSize = new Size(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
            printDialog.PrintDocument(paginator, "Reporte de nómina");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo imprimir el reporte de nómina.");
            MessageBox.Show(this, $"No se pudo imprimir: {ex.Message}", "No se pudo imprimir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Pedido explícito del usuario: "que te pregunte en qué carpeta quieres
    /// guardar las exportaciones ... no que luego luego lo mande a descargas" — SaveFileDialog
    /// siempre pregunta dónde guardar, nunca elige una carpeta fija por su cuenta.</summary>
    private void OnExportExcelClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"reporte-nomina-{DateTime.Now:yyyy-MM-dd}.xlsx",
            Filter = "Libro de Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            PayrollExcelExporter.Save(dialog.FileName, _rows, _weekRangeText, _branchFilterText);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo exportar el reporte de nómina a Excel.");
            MessageBox.Show(this, $"No se pudo exportar a Excel: {ex.Message}", "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Exporta a PDF con la impresora virtual "Microsoft Print to PDF" que trae
    /// Windows 10/11 de fábrica — reutiliza el MISMO documento/paginador que "Imprimir", así
    /// el PDF sale idéntico a lo impreso, sin agregar ninguna librería de PDF nueva al
    /// proyecto. Esa impresora, al recibir un trabajo, SIEMPRE pregunta dónde guardar el
    /// archivo (su puerto es "PORTPROMPT:") — mismo pedido explícito del usuario de no
    /// mandar la exportación a una carpeta fija sin preguntar.
    ///
    /// NUNCA confirmado contra una instalación real de Windows (este proyecto se desarrolla
    /// en macOS, sin acceso a hardware/SO real de prueba) — si la impresora no existe o el
    /// usuario la desactivó, se avisa con un mensaje claro en vez de fallar en silencio.</summary>
    private void OnExportPdfClick(object sender, RoutedEventArgs e)
    {
        PrintQueue? pdfQueue;
        try
        {
            using var printServer = new LocalPrintServer();
            pdfQueue = printServer.GetPrintQueues().FirstOrDefault(
                q => q.Name.Equals("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudieron consultar las impresoras instaladas para exportar a PDF.");
            MessageBox.Show(this, $"No se pudo consultar las impresoras instaladas: {ex.Message}",
                "No se pudo exportar a PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (pdfQueue is null)
        {
            MessageBox.Show(
                this,
                "No se encontró la impresora \"Microsoft Print to PDF\" en este equipo.\n\n" +
                "Actívala en Windows (Configuración > Bluetooth y dispositivos > Impresoras y " +
                "escáneres > Agregar dispositivo > Microsoft Print to PDF) o usa \"Imprimir\" y elige " +
                "esa impresora ahí mismo.",
                "No se pudo exportar a PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var writer = PrintQueue.CreateXpsDocumentWriter(pdfQueue);
            var paginator = ((IDocumentPaginatorSource)_document).DocumentPaginator;
            writer.Write(paginator);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo exportar el reporte de nómina a PDF.");
            MessageBox.Show(this, $"No se pudo exportar a PDF: {ex.Message}", "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
