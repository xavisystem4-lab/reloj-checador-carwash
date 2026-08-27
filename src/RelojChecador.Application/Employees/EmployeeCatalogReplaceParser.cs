using System.Globalization;
using RelojChecador.Application.Common;
using RelojChecador.Domain.Employees;

namespace RelojChecador.Application.Employees;

/// <summary>Una fila ya interpretada del catálogo de reemplazo — pura interpretación de
/// texto, sin tocar la base de datos (ver <see cref="EmployeeCatalogReplaceParser"/>). Igual
/// que <see cref="EmployeeImportRow"/>, cruzar esto contra la base local real (¿ya existe
/// alguien con este nombre? ¿la sucursal existe?) pasa después, en
/// EmployeesViewModel.PrepareCatalogReplacePreviewAsync.</summary>
public sealed record EmployeeCatalogRow(
    int LineNumber,
    string Number,
    string FullName,
    string Area,
    string? Position,
    DateOnly? HireDate,
    EmploymentStatus Status,
    decimal? WeeklySalary,
    decimal? OvertimeHourlyRate,
    string? Notes,
    IReadOnlyList<string> Alerts)
{
    public bool HasAlerts => Alerts.Count > 0;
}

public sealed record EmployeeCatalogParseResult(IReadOnlyList<EmployeeCatalogRow> Rows, IReadOnlyList<string> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Parsea el catálogo "maestro" que REEMPLAZA la nómina completa — a diferencia de
/// <see cref="EmployeeImportParser"/> (que solo agrega gente nueva, nunca toca a quien ya
/// existe, por diseño explícito), este formato es más rico y alimenta un flujo que
/// ACTUALIZA a quien ya existe y coincide por nombre, CREA a quien es nuevo, y DA DE BAJA
/// (lógica, nunca borra) a quien ya no aparece — pedido explícito del usuario: "el excel que
/// te pasé es el único registro que quiero actualmente". Columnas esperadas:
/// <c>Number,FullName,Area,Position,HireDate,Status,WeeklySalary,OvertimeHourlyRate,Notes</c>.
///
/// <c>HireDate</c> (formato ISO <c>yyyy-MM-dd</c>) es OPCIONAL a propósito: para un empleado
/// nuevo, vacío usa la fecha de hoy (mismo criterio que EmployeeImportParser); para uno que
/// ya existe, vacío significa "no toques la fecha de ingreso que ya tiene guardada" — nunca
/// se asume una fecha que no se conoce de verdad.
///
/// <c>Status</c> acepta "Activo"/"Inactivo" (vacío = Activo) — mapea 1:1 a
/// <see cref="EmploymentStatus.Active"/>/<see cref="EmploymentStatus.Inactive"/>, igual
/// criterio que el filtro de estatus ya existente en EmployeesViewModel.MapStatusFilter.
/// Nunca acepta "Baja"/Terminated aquí — esa transición la decide el propio flujo de
/// reemplazo (quien no aparece en el catálogo), no algo que el archivo declare por fila.
/// </summary>
public static class EmployeeCatalogReplaceParser
{
    private static readonly string[] ExpectedHeader =
        ["Number", "FullName", "Area", "Position", "HireDate", "Status", "WeeklySalary", "OvertimeHourlyRate", "Notes"];

    public static EmployeeCatalogParseResult Parse(IReadOnlyList<string> lines)
    {
        var rows = new List<EmployeeCatalogRow>();
        var errors = new List<string>();

        if (lines.Count == 0)
        {
            errors.Add("El archivo está vacío.");
            return new EmployeeCatalogParseResult(rows, errors);
        }

        var header = CsvLineParser.SplitLine(lines[0]);
        if (!HeaderMatches(header))
        {
            errors.Add($"El encabezado no coincide con el esperado ({string.Join(",", ExpectedHeader)}).");
            return new EmployeeCatalogParseResult(rows, errors);
        }

        var seenNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue; // línea en blanco al final del archivo, no es un error
            }

            var lineNumber = i + 1;
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
            var notes = NullIfEmpty(fields[8]);

            if (number.Length == 0 || fullName.Length == 0 || area.Length == 0)
            {
                errors.Add($"Línea {lineNumber}: Number, FullName y Area son obligatorios.");
                continue;
            }

            if (!seenNumbers.Add(number))
            {
                errors.Add($"Línea {lineNumber}: el número \"{number}\" ya aparece antes en este mismo archivo.");
                continue;
            }

            if (!TryParseOptionalHireDate(fields[4], out var hireDate))
            {
                errors.Add($"Línea {lineNumber}: HireDate \"{fields[4]}\" no es una fecha válida (formato AAAA-MM-DD, o vacío).");
                continue;
            }

            if (!TryParseStatus(fields[5], out var status))
            {
                errors.Add($"Línea {lineNumber}: Status \"{fields[5]}\" debe ser \"Activo\", \"Inactivo\" o estar vacío.");
                continue;
            }

            if (!TryParseOptionalDecimal(fields[6], out var weeklySalary))
            {
                errors.Add($"Línea {lineNumber}: WeeklySalary \"{fields[6]}\" no es un número válido (déjalo vacío si se desconoce).");
                continue;
            }

            if (!TryParseOptionalDecimal(fields[7], out var overtimeHourlyRate))
            {
                errors.Add($"Línea {lineNumber}: OvertimeHourlyRate \"{fields[7]}\" no es un número válido (déjalo vacío si no aplica).");
                continue;
            }

            var alerts = new List<string>();
            if (weeklySalary is null)
            {
                alerts.Add("Sueldo semanal pendiente de captura.");
            }

            rows.Add(new EmployeeCatalogRow(
                lineNumber, number, fullName, area, position, hireDate, status,
                weeklySalary, overtimeHourlyRate, notes, alerts));
        }

        return new EmployeeCatalogParseResult(rows, errors);
    }

    private static bool HeaderMatches(string[] header) =>
        header.Length == ExpectedHeader.Length
        && header.Select(h => h.Trim()).SequenceEqual(ExpectedHeader, StringComparer.OrdinalIgnoreCase);

    private static bool TryParseOptionalHireDate(string raw, out DateOnly? value)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseStatus(string raw, out EmploymentStatus status)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || string.Equals(trimmed, "Activo", StringComparison.OrdinalIgnoreCase))
        {
            status = EmploymentStatus.Active;
            return true;
        }

        if (string.Equals(trimmed, "Inactivo", StringComparison.OrdinalIgnoreCase))
        {
            status = EmploymentStatus.Inactive;
            return true;
        }

        status = default;
        return false;
    }

    /// <summary>Vacío → <c>null</c> ("no capturado"). Nunca se interpreta un vacío como
    /// <c>0</c> — mismo criterio que EmployeeImportParser.</summary>
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
