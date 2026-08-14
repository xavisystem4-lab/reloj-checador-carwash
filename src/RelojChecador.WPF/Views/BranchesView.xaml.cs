using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.Win32;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Views;

public partial class BranchesView : UserControl
{
    public BranchesView()
    {
        InitializeComponent();
    }

    private async void OnAddBranchClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new AddBranchDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var error = await viewModel.CreateBranchAsync(
            dialog.Code, dialog.BranchName, dialog.TimeZoneId, dialog.LegalEntityName, dialog.Address);

        if (error is not null)
        {
            MessageBox.Show(Window.GetWindow(this), error, "No se pudo crear la sucursal", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Mismo patrón que AttendanceView/PayrollView.OnExportCsvClick: el
    /// ViewModel arma el texto (BuildCsv), aquí solo se resuelve dónde guardarlo.</summary>
    private void OnExportCsvClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Branches.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "No hay sucursales registradas para exportar.",
                "Nada que exportar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            FileName = "sucursales.csv",
            Filter = "Archivo CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // BOM UTF-8 explícito (mismo criterio que AttendanceView/PayrollView/Dashboard
            // web) para que Excel reconozca acentos/eñes sin pedir la codificación a mano.
            File.WriteAllText(dialog.FileName, viewModel.BuildCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se pudo guardar el archivo: {ex.Message}",
                "No se pudo exportar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Primer uso de impresión en el proyecto — deliberadamente vía el
    /// PrintDialog nativo de WPF (FlowDocument + DocumentPaginator) en vez de una
    /// librería de PDF de terceros: Windows 10/11 trae de fábrica el driver virtual
    /// "Microsoft Print to PDF", así que el mismo diálogo sirve tanto para imprimir en
    /// papel como para "exportar a PDF" (el usuario elige esa impresora virtual y guarda
    /// el archivo) — cero dependencias nuevas en el .exe self-contained. Patrón queda
    /// disponible para reusarse en otras pantallas si se pide más adelante.</summary>
    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (viewModel.Branches.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this),
                "No hay sucursales registradas para imprimir.",
                "Nada que imprimir", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var document = BuildPrintDocument(viewModel);
            document.PageHeight = printDialog.PrintableAreaHeight;
            document.PageWidth = printDialog.PrintableAreaWidth;
            document.PagePadding = new Thickness(40);

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            printDialog.PrintDocument(paginator, "Sucursales");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"No se pudo imprimir: {ex.Message}",
                "No se pudo imprimir", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Arma un documento imprimible sencillo: título, fecha y una tabla con las
    /// mismas columnas que la grilla en pantalla. Solo texto plano (sin gráficos ni
    /// estilos del tema oscuro/claro de la app — un documento impreso siempre se ve en
    /// fondo blanco con texto negro, sin importar qué tema tenga la app abierta).</summary>
    private static FlowDocument BuildPrintDocument(MainViewModel viewModel)
    {
        var document = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            Foreground = Brushes.Black,
        };

        document.Blocks.Add(new Paragraph(new Run("Sucursales"))
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        document.Blocks.Add(new Paragraph(new Run($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}"))
        {
            FontSize = 11,
            Foreground = Brushes.DimGray,
            Margin = new Thickness(0, 0, 0, 16),
        });

        var table = new Table();
        for (var i = 0; i < 5; i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow { Background = Brushes.WhiteSmoke, FontWeight = FontWeights.Bold };
        foreach (var header in new[] { "Código", "Nombre", "Zona horaria", "Razón social", "Activa" })
        {
            headerRow.Cells.Add(BuildCell(header));
        }
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();
        foreach (var branch in viewModel.Branches)
        {
            var row = new TableRow();
            row.Cells.Add(BuildCell(branch.Code));
            row.Cells.Add(BuildCell(branch.Name));
            row.Cells.Add(BuildCell(branch.TimeZoneId));
            row.Cells.Add(BuildCell(branch.LegalEntityName ?? "—"));
            row.Cells.Add(BuildCell(branch.IsActive ? "Sí" : "No"));
            bodyGroup.Rows.Add(row);
        }
        table.RowGroups.Add(bodyGroup);

        document.Blocks.Add(table);
        return document;
    }

    private static TableCell BuildCell(string text) => new(new Paragraph(new Run(text))
    {
        Margin = new Thickness(0),
        Padding = new Thickness(6, 4, 6, 4),
    })
    {
        BorderBrush = Brushes.LightGray,
        BorderThickness = new Thickness(0, 0, 0, 1),
    };
}
