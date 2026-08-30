using System.Globalization;
using ClosedXML.Excel;
using RelojChecador.Application.Employees;

namespace RelojChecador.WPF.Services;

/// <summary>
/// Lee un .xlsx para "Reemplazar catálogo" y lo deja como header + filas de texto plano,
/// listo para <see cref="EmployeeCatalogSourceConverter.TryConvert"/> — misma idea que ya
/// hacía CsvLineParser para un .csv, pero resolviendo además dos cosas propias de Excel que un
/// CSV no tiene:
///
/// 1) La fila de encabezado real no es necesariamente la primera: el Excel maestro del
///    negocio ("REGISTRO EJECUTIVO EMPLEADOS.xlsx", hoja "Registro Empleados") trae título e
///    instrucciones en las primeras filas antes de llegar a los encabezados de columna. Se
///    buscan los encabezados en las primeras <see cref="MaxHeaderSearchRows"/> filas de la
///    hoja usando <see cref="EmployeeCatalogSourceConverter.IsRecognizedHeader"/> — si ninguna
///    fila coincide con un formato conocido, se reporta error en vez de adivinar cuál fila es.
///
/// 2) Las celdas de Excel tienen tipo (fecha, número, texto) — se convierten aquí mismo a
///    texto invariante (fecha ISO <c>yyyy-MM-dd</c>, número con punto decimal) para que el
///    resto del flujo (EmployeeCatalogSourceConverter, EmployeeCatalogReplaceParser) siga
///    trabajando solo con texto, igual que con un CSV.
/// </summary>
public static class ExcelCatalogReader
{
    private const int MaxHeaderSearchRows = 15;

    public static bool TryRead(
        string filePath, out IReadOnlyList<string> header, out IReadOnlyList<IReadOnlyList<string?>> rows, out string? error)
    {
        header = [];
        rows = [];

        using var workbook = new XLWorkbook(filePath);
        var worksheet = workbook.Worksheets.FirstOrDefault(
            w => string.Equals(w.Name, "Registro Empleados", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault();

        if (worksheet is null)
        {
            error = "El archivo Excel no tiene ninguna hoja.";
            return false;
        }

        var usedRange = worksheet.RangeUsed();
        if (usedRange is null)
        {
            error = $"La hoja \"{worksheet.Name}\" está vacía.";
            return false;
        }

        var lastRow = usedRange.LastRow().RowNumber();
        var lastColumn = usedRange.LastColumn().ColumnNumber();

        var headerRowNumber = -1;
        for (var r = usedRange.FirstRow().RowNumber(); r <= Math.Min(lastRow, MaxHeaderSearchRows); r++)
        {
            var candidate = ReadRow(worksheet, r, lastColumn).Select(c => c ?? "").ToArray();
            if (EmployeeCatalogSourceConverter.IsRecognizedHeader(candidate))
            {
                header = candidate;
                headerRowNumber = r;
                break;
            }
        }

        if (headerRowNumber < 0)
        {
            error = $"No se encontró un encabezado reconocido en la hoja \"{worksheet.Name}\" (se buscó en las primeras " +
                $"{MaxHeaderSearchRows} filas).";
            return false;
        }

        var dataRows = new List<IReadOnlyList<string?>>();
        for (var r = headerRowNumber + 1; r <= lastRow; r++)
        {
            dataRows.Add(ReadRow(worksheet, r, lastColumn));
        }

        rows = dataRows;
        error = null;
        return true;
    }

    private static IReadOnlyList<string?> ReadRow(IXLWorksheet worksheet, int rowNumber, int lastColumn)
    {
        var cells = new string?[lastColumn];
        for (var c = 1; c <= lastColumn; c++)
        {
            cells[c - 1] = CellToText(worksheet.Cell(rowNumber, c));
        }
        return cells;
    }

    private static string CellToText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return "";
        }

        return cell.DataType switch
        {
            XLDataType.DateTime => cell.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            XLDataType.Number => cell.GetDouble().ToString("0.####", CultureInfo.InvariantCulture),
            _ => cell.GetString().Trim(),
        };
    }
}
