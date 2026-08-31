using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Services;

/// <summary>
/// Arma el FlowDocument del reporte de nómina para PayrollReportPreviewDialog — pedido
/// explícito del usuario: "¿Quieres que le agregue a Reportes ... la misma previsualización
/// tipo hoja con el logo, más los botones Imprimir/Exportar Excel/Exportar PDF, igual que ya
/// quedó en la web?" (respondido "Sí"). Mismo diseño que la previsualización del Dashboard
/// web (logotipo + encabezado + tabla), pero con controles nativos de WPF: Imprimir vía
/// PrintDialog y PDF vía la impresora virtual "Microsoft Print to PDF" (ver
/// PayrollReportPreviewDialog), reutilizando el MISMO FlowDocument/paginador para los dos —
/// así el PDF sale idéntico a lo impreso.
///
/// Tamaño Carta APAISADA (11in x 8.5in, no vertical como la web) — la nómina trae 12
/// columnas (incluye montos de ISR/IMSS/etc., que el reporte de horas de la web no
/// necesitaba mostrar), no caben cómodas en un ancho de 8.5in.
/// </summary>
public static class PayrollReportDocumentBuilder
{
    private static readonly (string Header, double Width, Func<PayrollRow, string> Value)[] Columns =
    [
        ("Empleado", 150, r => r.EmployeeName),
        ("Sucursal", 80, r => r.BranchName),
        ("Departamento", 90, r => r.Department ?? "—"),
        ("Horas normales", 70, r => r.RegularTimeText),
        ("Horas extra", 65, r => r.OvertimeTimeText),
        ("Sueldo semanal", 75, r => r.WeeklySalaryText),
        ("Pago horas extra", 75, r => r.Summary.OvertimePay.ToString("C")),
        ("Total a pagar", 75, r => r.Summary.TotalPay.ToString("C")),
        ("ISR", 60, r => r.Deductions.IsrAmount.ToString("C")),
        ("IMSS", 60, r => r.Deductions.ImssAmount.ToString("C")),
        ("Otro", 60, r => r.Deductions.OtherAmount.ToString("C")),
        ("Neto a pagar", 75, r => r.NetPay.ToString("C")),
    ];

    private static readonly Brush Brand = (Brush)new BrushConverter().ConvertFromString("#0B62A7")!;
    private static readonly Brush BrandDark = (Brush)new BrushConverter().ConvertFromString("#095183")!;
    private static readonly Brush Muted = (Brush)new BrushConverter().ConvertFromString("#5B6472")!;
    private static readonly Brush HeaderFill = (Brush)new BrushConverter().ConvertFromString("#E7F0FB")!;
    private static readonly Brush RowBorder = (Brush)new BrushConverter().ConvertFromString("#E1E7F0")!;
    private static readonly Brush BodyText = (Brush)new BrushConverter().ConvertFromString("#101828")!;

    public static FlowDocument Build(IReadOnlyList<PayrollRow> rows, string weekRangeText, string branchFilterText)
    {
        var document = new FlowDocument
        {
            PageWidth = 11 * 96,
            PageHeight = 8.5 * 96,
            PagePadding = new Thickness(40),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Background = Brushes.White,
            Foreground = BodyText,
        };

        document.Blocks.Add(new BlockUIContainer(BuildHeader(weekRangeText, branchFilterText))
        {
            Margin = new Thickness(0, 0, 0, 16),
        });

        document.Blocks.Add(BuildTable(rows));

        document.Blocks.Add(new Paragraph(new Run($"Generado el {DateTime.Now:dd/MM/yyyy HH:mm}"))
        {
            FontSize = 9,
            Foreground = Muted,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        });

        return document;
    }

    private static UIElement BuildHeader(string weekRangeText, string branchFilterText)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        try
        {
            panel.Children.Add(new Image
            {
                Source = new BitmapImage(new Uri("pack://application:,,,/Assets/DriveInLogo.png")),
                Height = 48,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 16, 0),
            });
        }
        catch
        {
            // Si el logotipo no carga por algún motivo, el reporte se sigue generando sin
            // él — no hay ningún motivo de negocio para bloquear el reporte por esto.
        }

        var textPanel = new StackPanel();
        textPanel.Children.Add(new TextBlock
        {
            Text = "Drive In Car Wash", FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brand,
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = "Reporte de nómina", FontSize = 20, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 2, 0, 0),
        });
        textPanel.Children.Add(new TextBlock
        {
            Text = $"Semana {weekRangeText} — {branchFilterText}",
            FontSize = 12, Foreground = Muted, Margin = new Thickness(0, 4, 0, 0),
        });
        panel.Children.Add(textPanel);

        return new Border
        {
            Child = panel,
            BorderBrush = Brand,
            BorderThickness = new Thickness(0, 0, 0, 3),
            Padding = new Thickness(0, 0, 0, 12),
        };
    }

    private static Table BuildTable(IReadOnlyList<PayrollRow> rows)
    {
        var table = new Table { CellSpacing = 0 };
        foreach (var column in Columns)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(column.Width, GridUnitType.Star) });
        }

        var headerGroup = new TableRowGroup();
        var headerRow = new TableRow();
        foreach (var column in Columns)
        {
            headerRow.Cells.Add(BuildCell(column.Header, isHeader: true));
        }
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new TableRowGroup();
        foreach (var row in rows)
        {
            var tableRow = new TableRow();
            foreach (var column in Columns)
            {
                tableRow.Cells.Add(BuildCell(column.Value(row), isHeader: false));
            }
            bodyGroup.Rows.Add(tableRow);
        }
        table.RowGroups.Add(bodyGroup);

        return table;
    }

    private static TableCell BuildCell(string text, bool isHeader)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontWeight = isHeader ? FontWeights.Bold : FontWeights.Normal,
            FontSize = 10,
            Foreground = isHeader ? BrandDark : BodyText,
            Margin = new Thickness(0),
        };
        return new TableCell(paragraph)
        {
            Padding = new Thickness(6, 5, 6, 5),
            Background = isHeader ? HeaderFill : Brushes.Transparent,
            BorderBrush = isHeader ? Brand : RowBorder,
            BorderThickness = isHeader ? new Thickness(0, 0, 0, 2) : new Thickness(0, 0, 0, 1),
        };
    }
}
