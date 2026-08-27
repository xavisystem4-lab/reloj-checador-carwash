using System.Globalization;
using RelojChecador.Application.Common;

namespace RelojChecador.Application.Employees;

/// <summary>Una fila ya interpretada de un CSV de importación masiva — pura interpretación
/// de texto, sin tocar ninguna base de datos (ver <see cref="EmployeeImportParser"/>).
/// Resolver si la sucursal ya existe, si el número ya está en uso, etc. pasa DESPUÉS,
/// contra la base local real (ver EmployeesViewModel.PrepareImportPreviewAsync) — este
/// tipo solo sabe lo que dice el archivo.</summary>
public sealed record EmployeeImportRow(
    int LineNumber,
    string Number,
    string FullName,
    string Area,
    string? Position,
    decimal? WeeklySalary,
    decimal? OvertimeHourlyRate,
    string? Notes,
    IReadOnlyList<string> Alerts)
{
    public bool HasAlerts => Alerts.Count > 0;
}

/// <summary>Resultado de parsear un archivo completo — filas válidas más errores de
/// formato por fila. Un error en una línea nunca aborta el resto del archivo (mismo
/// criterio defensivo que WorkedHoursCalculator: nunca inventa, nunca calla un
/// problema).</summary>
public sealed record EmployeeImportResult(IReadOnlyList<EmployeeImportRow> Rows, IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Parsea el CSV de importación masiva de empleados — lógica pura sin dependencias de
/// infraestructura (mismo criterio que <c>WorkedHoursCalculator</c>, para poder probarla
/// exhaustivamente con xUnit). Columnas esperadas:
/// <c>Number,FullName,Area,Position,WeeklySalary,OvertimeHourlyRate,Notes</c> —
/// Position/WeeklySalary/OvertimeHourlyRate/Notes pueden ir vacíos; un
/// <c>WeeklySalary</c>/<c>OvertimeHourlyRate</c> vacío se interpreta como
/// <c>null</c> ("pendiente de captura"), NUNCA como <c>0</c> — caso real que motivó esta
/// función: importar un catálogo real donde varios empleados no tenían sueldo confirmado
/// en ninguna fuente.
/// </summary>
public static class EmployeeImportParser
{
    private static readonly string[] ExpectedHeader =
        ["Number", "FullName", "Area", "Position", "WeeklySalary", "OvertimeHourlyRate", "Notes"];

    public static EmployeeImportResult Parse(IReadOnlyList<string> lines)
    {
        var rows = new List<EmployeeImportRow>();
        var errors = new List<string>();

        if (lines.Count == 0)
        {
            errors.Add("El archivo está vacío.");
            return new EmployeeImportResult(rows, errors);
        }

        var header = CsvLineParser.SplitLine(lines[0]);
        if (!HeaderMatches(header))
        {
            errors.Add($"El encabezado no coincide con el esperado ({string.Join(",", ExpectedHeader)}).");
            return new EmployeeImportResult(rows, errors);
        }

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue; // línea en blanco al final del archivo, no es un error
            }

            var lineNumber = i + 1; // 1-based, coincide con lo que vería el usuario al abrir el archivo
            var fields = CsvLineParser.SplitLine(line);
            if (fields.Length != ExpectedHeader.Length)
            {
                errors.Add($"Línea {lineNumber}: se esperaban {ExpectedHeader.Length} columnas, se encontraron {fields.Length}.");
                continue;
            }

            var number = fields[0].Trim();
            var fullName = fields[1].Trim();
            var area = fields[2].Trim();
            var position = NullIfEmpty(fields[3]);
            var notes = NullIfEmpty(fields[6]);

            if (number.Length == 0 || fullName.Length == 0 || area.Length == 0)
            {
                errors.Add($"Línea {lineNumber}: Number, FullName y Area son obligatorios.");
                continue;
            }

            if (!TryParseOptionalDecimal(fields[4], out var weeklySalary))
            {
                errors.Add($"Línea {lineNumber}: WeeklySalary \"{fields[4]}\" no es un número válido (déjalo vacío si se desconoce).");
                continue;
            }

            if (!TryParseOptionalDecimal(fields[5], out var overtimeHourlyRate))
            {
                errors.Add($"Línea {lineNumber}: OvertimeHourlyRate \"{fields[5]}\" no es un número válido (déjalo vacío si no aplica).");
                continue;
            }

            var alerts = new List<string>();
            if (weeklySalary is null)
            {
                alerts.Add("Sueldo semanal pendiente de captura.");
            }
            if (string.Equals(position, "SIN PUESTO", StringComparison.OrdinalIgnoreCase))
            {
                alerts.Add("Sin puesto definido.");
            }

            rows.Add(new EmployeeImportRow(
                lineNumber, number, fullName, area, position, weeklySalary, overtimeHourlyRate, notes, alerts));
        }

        return new EmployeeImportResult(rows, errors);
    }

    private static bool HeaderMatches(string[] header) =>
        header.Length == ExpectedHeader.Length
        && header.Select(h => h.Trim()).SequenceEqual(ExpectedHeader, StringComparer.OrdinalIgnoreCase);

    /// <summary>Vacío → <c>null</c> ("no capturado"). Nunca se interpreta un vacío como
    /// <c>0</c> — ver comentario de clase.</summary>
    private static bool TryParseOptionalDecimal(string raw, out decimal? value)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
