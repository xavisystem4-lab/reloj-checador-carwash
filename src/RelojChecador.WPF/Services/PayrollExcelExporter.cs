using ClosedXML.Excel;
using RelojChecador.WPF.ViewModels;

namespace RelojChecador.WPF.Services;

/// <summary>
/// Escribe el reporte de nómina (exactamente lo que esté visible en Reportes en ese
/// momento) como .xlsx real — pedido explícito del usuario: "Exportar en Excel", igual
/// criterio que ya se usó en el Dashboard web (SheetJS) pero con ClosedXML, que este
/// proyecto ya trae para LEER catálogos (ver ExcelCatalogReader) — sin agregar ninguna
/// dependencia nueva al .exe self-contained.
/// </summary>
public static class PayrollExcelExporter
{
    private static readonly string[] Headers =
    [
        "Empleado", "Sucursal", "Departamento", "Horas normales", "Horas extra",
        "Sueldo semanal", "Pago horas extra", "Total a pagar", "ISR", "IMSS", "Otro", "Neto a pagar",
    ];

    public static void Save(string filePath, IReadOnlyList<PayrollRow> rows, string weekRangeText, string branchFilterText)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Nómina");

        sheet.Cell(1, 1).Value = "Drive In Car Wash — Reporte de nómina";
        sheet.Cell(1, 1).Style.Font.Bold = true;
        sheet.Cell(1, 1).Style.Font.FontSize = 14;
        sheet.Cell(2, 1).Value = $"Semana {weekRangeText} — {branchFilterText}";
        sheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#5B6472");

        const int headerRowIndex = 4;
        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = sheet.Cell(headerRowIndex, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E7F0FB");
        }

        var rowIndex = headerRowIndex + 1;
        foreach (var row in rows)
        {
            sheet.Cell(rowIndex, 1).Value = row.EmployeeName;
            sheet.Cell(rowIndex, 2).Value = row.BranchName;
            sheet.Cell(rowIndex, 3).Value = row.Department ?? "";
            sheet.Cell(rowIndex, 4).Value = row.RegularTimeText;
            sheet.Cell(rowIndex, 5).Value = row.OvertimeTimeText;

            // "Pendiente" en vez de $0.00 cuando el sueldo no está capturado — mismo
            // criterio que WeeklySalaryText (ver comentario de clase de PayrollRow):
            // nunca se inventa un valor que no se conoce de verdad.
            if (row.Summary.WeeklySalary is { } salary)
            {
                sheet.Cell(rowIndex, 6).Value = salary;
            }
            else
            {
                sheet.Cell(rowIndex, 6).Value = "Pendiente";
            }

            sheet.Cell(rowIndex, 7).Value = row.Summary.OvertimePay;
            sheet.Cell(rowIndex, 8).Value = row.Summary.TotalPay;
            sheet.Cell(rowIndex, 9).Value = row.Deductions.IsrAmount;
            sheet.Cell(rowIndex, 10).Value = row.Deductions.ImssAmount;
            sheet.Cell(rowIndex, 11).Value = row.Deductions.OtherAmount;
            sheet.Cell(rowIndex, 12).Value = row.NetPay;
            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }
}
