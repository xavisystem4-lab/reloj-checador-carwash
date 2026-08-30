using System.Globalization;
using System.Linq;
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
    string? Pin,
    string? Department,
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
/// <c>Number,FullName,Area,Position,HireDate,Status,WeeklySalary,OvertimeHourlyRate,Notes,Pin,Department</c>.
///
/// <c>Department</c> (agregada a pedido explícito del usuario, tras fusionar varias
/// sucursales en una sola: "que en los reportes se acomoden por sucursal") es OPCIONAL —
/// guarda la ubicación/sucursal ORIGINAL de alguien cuando ya no coincide con su Area actual
/// (p. ej. alguien fusionado de "Arabica Café" hacia "CAR-WASH" conserva "Arabica Café" aquí),
/// para no perder ese dato en Asistencia/Reportes aunque administrativamente ya solo exista
/// una sucursal. Vacío = sin diferencia que registrar.
///
/// <c>Pin</c> (agregada a pedido explícito del usuario: "quiero que agregue la columna PIN
/// ... para que el PIN lo detecte el sistema al importarlo") es OPCIONAL y, si viene, debe
/// ser solo dígitos — el teclado del reloj checador es numérico, igual criterio que
/// DevicesViewModel.SendEmployeesToDeviceAsync al asignar PIN automático. Esta clase solo
/// valida el formato; vincularlo de verdad (crear/actualizar EmployeeDeviceMapping, y que
/// "Enviar empleados al reloj" lo suba de verdad al dispositivo) pasa después, en
/// EmployeesViewModel.ApplyCatalogReplaceAsync, que también valida que el PIN no choque con
/// el de otro empleado en el mismo reloj.
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
        ["Number", "FullName", "Area", "Position", "HireDate", "Status", "WeeklySalary", "OvertimeHourlyRate", "Notes", "Pin", "Department"];

    /// <summary>Encabezado + una fila de ejemplo, en el formato exacto que espera
    /// <see cref="Parse"/> — única fuente para el botón "Exportar plantilla", tanto en
    /// ReplaceEmployeeCatalogDialog como el atajo directo en la pantalla de Empleados (ver
    /// EmployeesView.xaml.cs), para no mantener el mismo texto duplicado en dos archivos.</summary>
    public static readonly string SampleTemplateCsv =
        string.Join(",", ExpectedHeader) + "\r\n" +
        "EMP-001,Nombre Ejemplo,Drive In Car Wash,Puesto ejemplo,2024-01-15,Activo,3500,125,Borra esta fila de ejemplo antes de importar,1,\r\n";

    public static EmployeeCatalogParseResult Parse(IReadOnlyList<string> lines)
    {
        var rows = new List<EmployeeCatalogRow>();
        var errors = new List<string>();

        if (lines.Count == 0)
        {
            errors.Add("El archivo está vacío.");
            return new EmployeeCatalogParseResult(rows, errors);
        }

        // Detectado UNA VEZ sobre el encabezado y usado para todo el archivo — ver
        // CsvLineParser.DetectDelimiter (Excel en español exporta CSV con ';' en vez de
        // ',').
        var delimiter = CsvLineParser.DetectDelimiter(lines[0]);
        var header = CsvLineParser.SplitLine(lines[0], delimiter);
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
            var fields = CsvLineParser.SplitLine(line, delimiter);
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
            var department = NullIfEmpty(fields[10]);

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
                errors.Add($"Línea {lineNumber}: HireDate \"{fields[4]}\" no es una fecha válida (formato AAAA-MM-DD o DD/MM/AAAA, o vacío).");
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

            if (!TryParseOptionalPin(fields[9], out var pin))
            {
                errors.Add($"Línea {lineNumber}: Pin \"{fields[9]}\" debe ser solo dígitos (el teclado del reloj es numérico), o estar vacío.");
                continue;
            }

            var alerts = new List<string>();
            if (weeklySalary is null)
            {
                alerts.Add("Sueldo semanal pendiente de captura.");
            }

            rows.Add(new EmployeeCatalogRow(
                lineNumber, number, fullName, area, position, hireDate, status,
                weeklySalary, overtimeHourlyRate, notes, pin, department, alerts));
        }

        return new EmployeeCatalogParseResult(rows, errors);
    }

    private static bool HeaderMatches(string[] header) =>
        header.Length == ExpectedHeader.Length
        && header.Select(h => h.Trim()).SequenceEqual(ExpectedHeader, StringComparer.OrdinalIgnoreCase);

    /// <summary>El formato preferido sigue siendo ISO <c>yyyy-MM-dd</c>, pero también se
    /// acepta <c>dd/MM/yyyy</c> (Español México, pedido explícito del usuario) — mismo
    /// criterio "día antes que mes" que el resto de la UI en español de este proyecto. A
    /// propósito NO se intenta adivinar <c>MM/dd/yyyy</c> (inglés EE. UU.): con dos formatos
    /// de barras aceptados a la vez, un valor como "07/12/2023" sería ambiguo entre 7 de
    /// diciembre y 12 de julio, y adivinar mal correría el riesgo de guardar una fecha de
    /// ingreso equivocada en silencio — peor que rechazar la fila con un error claro.
    ///
    /// Aparte de esos dos formatos de texto, también se acepta el número de serie de fecha
    /// de Excel (días desde 1899-12-30): cuando alguien abre y vuelve a guardar el CSV en
    /// Excel, este a veces "ayuda" reinterpretando el texto como fecha real y, si la celda
    /// quedó en formato "General", lo exporta como ese número entero (p. ej. <c>45267</c>)
    /// en vez de texto — a diferencia de un formato de barras ambiguo, esta conversión sí es
    /// exacta y sin ambigüedad.</summary>
    private static bool TryParseOptionalHireDate(string raw, out DateOnly? value)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        string[] acceptedFormats = ["yyyy-MM-dd", "dd/MM/yyyy"];
        if (DateOnly.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed;
            return true;
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 100_000)
        {
            value = DateOnly.FromDateTime(new DateTime(1899, 12, 30).AddDays(serial));
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

    /// <summary>Vacío → <c>null</c> ("sin vincular a un reloj todavía"). Si viene, debe ser
    /// solo dígitos — el teclado del reloj checador es numérico, un PIN con letras o
    /// símbolos (p. ej. "EMP-001") lo rechazaría de verdad al enrolar, mismo motivo por el
    /// que "Enviar empleados al reloj" nunca usa el Number del negocio como PIN.</summary>
    private static bool TryParseOptionalPin(string raw, out string? value)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (trimmed.All(char.IsDigit))
        {
            value = trimmed;
            return true;
        }

        value = null;
        return false;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
