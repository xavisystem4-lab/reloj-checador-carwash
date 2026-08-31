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
    TimeOnly? ScheduledStartTime,
    TimeOnly? ScheduledEndTime,
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
/// te pasé es el único registro que quiero actualmente". Columnas mínimas obligatorias:
/// <c>Number,FullName,Area</c>; el resto son opcionales y se buscan POR NOMBRE en el
/// encabezado, no por posición fija — así el mismo parser acepta tanto el catálogo
/// "clásico" (con <c>Department</c>) como uno más nuevo con horario (<c>Hora Entrada</c>,
/// <c>Hora Salida</c>) al mismo tiempo, sin que uno excluya al otro, y cualquier columna
/// vacía o desconocida (p. ej. una coma sobrante al final típica de guardar desde Excel) se
/// ignora en vez de rechazar el archivo entero. Ver <see cref="KnownColumns"/> para la
/// lista completa reconocida.
///
/// <c>Department</c> (agregada a pedido explícito del usuario, tras fusionar varias
/// sucursales en una sola: "que en los reportes se acomoden por sucursal") es OPCIONAL —
/// guarda la ubicación/sucursal ORIGINAL de alguien cuando ya no coincide con su Area actual
/// (p. ej. alguien fusionado de "Arabica Café" hacia "CAR-WASH" conserva "Arabica Café" aquí),
/// para no perder ese dato en Asistencia/Reportes aunque administrativamente ya solo exista
/// una sucursal. Vacío = sin diferencia que registrar.
///
/// <c>Hora Entrada</c>/<c>Hora Salida</c> (agregadas a pedido explícito del usuario, junto
/// con Employee.ScheduledStartTime/ScheduledEndTime: "que en Empleados me aparezcan sus
/// horarios") son OPCIONALES y deben venir juntas (una sin la otra es un error de fila) —
/// aceptan tanto 12 horas con AM/PM ("8:00 AM") como 24 horas ("16:00"). Igual que
/// Department, un archivo que no las trae para una fila que ya existe NUNCA borra un
/// horario ya capturado (ver EmployeesViewModel.ApplyCatalogReplaceAsync).
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
    private const string ColNumber = "Number";
    private const string ColFullName = "FullName";
    private const string ColArea = "Area";
    private const string ColPosition = "Position";
    private const string ColHireDate = "HireDate";
    private const string ColStatus = "Status";
    private const string ColWeeklySalary = "WeeklySalary";
    private const string ColOvertimeHourlyRate = "OvertimeHourlyRate";
    private const string ColNotes = "Notes";
    private const string ColPin = "Pin";
    private const string ColDepartment = "Department";
    private const string ColScheduledStartTime = "Hora Entrada";
    private const string ColScheduledEndTime = "Hora Salida";

    private static readonly string[] RequiredColumns = [ColNumber, ColFullName, ColArea];

    private static readonly string[] KnownColumns =
    [
        ColNumber, ColFullName, ColArea, ColPosition, ColHireDate, ColStatus, ColWeeklySalary,
        ColOvertimeHourlyRate, ColNotes, ColPin, ColDepartment, ColScheduledStartTime, ColScheduledEndTime,
    ];

    /// <summary>Encabezado "clásico" (con Department, sin horario) + una fila de ejemplo —
    /// única fuente para el botón "Exportar plantilla", tanto en ReplaceEmployeeCatalogDialog
    /// como el atajo directo en la pantalla de Empleados (ver EmployeesView.xaml.cs). Sigue
    /// siendo un formato válido para <see cref="Parse"/> aunque ya no sea el único — ver
    /// comentario de clase.</summary>
    public static readonly string SampleTemplateCsv =
        string.Join(",", ColNumber, ColFullName, ColArea, ColPosition, ColHireDate, ColStatus,
            ColWeeklySalary, ColOvertimeHourlyRate, ColNotes, ColPin, ColDepartment) + "\r\n" +
        "EMP-001,Nombre Ejemplo,Drive In Car Wash,Puesto ejemplo,2024-01-15,Activo,3500,125,Borra esta fila de ejemplo antes de importar,1,\r\n";

    /// <summary>True si <paramref name="header"/> ya trae lo mínimo que <see cref="Parse"/>
    /// necesita (Number/FullName/Area) y ninguna columna que no se reconozca — usado por
    /// EmployeeCatalogSourceConverter.IsCanonicalHeader para decidir si un archivo se le
    /// puede pasar tal cual a <see cref="Parse"/> o si primero hay que convertirlo (viene de
    /// otro formato de origen, p. ej. el Excel maestro). Comparte el mismo criterio "por
    /// nombre, no por posición" que <see cref="Parse"/> — así un archivo que ya pasa esta
    /// prueba nunca se rechaza después con un error de encabezado real.</summary>
    public static bool HasUsableHeader(IReadOnlyList<string> header)
    {
        var columnIndex = BuildColumnIndex(header);
        return RequiredColumns.All(columnIndex.ContainsKey)
            && columnIndex.Keys.All(name => KnownColumns.Contains(name, StringComparer.OrdinalIgnoreCase));
    }

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
        var headerFields = CsvLineParser.SplitLine(lines[0], delimiter);
        var columnIndex = BuildColumnIndex(headerFields);

        // Dos formas de rechazar un encabezado: le falta algo obligatorio, o trae columnas
        // que no se reconocen (típicamente un archivo de otro formato por completo, p. ej.
        // "Nombre" en vez de "FullName" — sin este chequeo se aceptaría en silencio con
        // Number/FullName/Area vacíos en vez de avisar del problema real).
        var missingRequired = RequiredColumns.Where(c => !columnIndex.ContainsKey(c)).ToList();
        var unknownColumns = columnIndex.Keys.Where(name => !KnownColumns.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList();
        if (missingRequired.Count > 0 || unknownColumns.Count > 0)
        {
            var problems = new List<string>();
            if (missingRequired.Count > 0)
            {
                problems.Add($"le faltan columnas obligatorias: {string.Join(", ", missingRequired)}");
            }
            if (unknownColumns.Count > 0)
            {
                problems.Add($"tiene columnas que no se reconocen: {string.Join(", ", unknownColumns)}");
            }
            errors.Add($"El encabezado {string.Join("; ", problems)}. Columnas reconocidas: {string.Join(", ", KnownColumns)}.");
            return new EmployeeCatalogParseResult(rows, errors);
        }

        var headerColumnCount = headerFields.Length;
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
            if (fields.Length != headerColumnCount)
            {
                errors.Add($"Línea {lineNumber}: se esperaban {headerColumnCount} columnas (igual que el encabezado), se encontraron {fields.Length}.");
                continue;
            }

            string Get(string columnName) =>
                columnIndex.TryGetValue(columnName, out var idx) && idx < fields.Length ? fields[idx] : "";

            var number = Get(ColNumber).Trim();
            var fullName = Get(ColFullName).Trim();
            var area = Get(ColArea).Trim();
            var position = NullIfEmpty(Get(ColPosition));
            var notes = NullIfEmpty(Get(ColNotes));
            var department = NullIfEmpty(Get(ColDepartment));

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

            if (!TryParseOptionalHireDate(Get(ColHireDate), out var hireDate))
            {
                errors.Add($"Línea {lineNumber}: HireDate \"{Get(ColHireDate)}\" no es una fecha válida (formato AAAA-MM-DD o DD/MM/AAAA, o vacío).");
                continue;
            }

            if (!TryParseStatus(Get(ColStatus), out var status))
            {
                errors.Add($"Línea {lineNumber}: Status \"{Get(ColStatus)}\" debe ser \"Activo\", \"Inactivo\" o estar vacío.");
                continue;
            }

            if (!TryParseOptionalDecimal(Get(ColWeeklySalary), out var weeklySalary))
            {
                errors.Add($"Línea {lineNumber}: WeeklySalary \"{Get(ColWeeklySalary)}\" no es un número válido (déjalo vacío si se desconoce).");
                continue;
            }

            if (!TryParseOptionalDecimal(Get(ColOvertimeHourlyRate), out var overtimeHourlyRate))
            {
                errors.Add($"Línea {lineNumber}: OvertimeHourlyRate \"{Get(ColOvertimeHourlyRate)}\" no es un número válido (déjalo vacío si no aplica).");
                continue;
            }

            if (!TryParseOptionalPin(Get(ColPin), out var pin))
            {
                errors.Add($"Línea {lineNumber}: Pin \"{Get(ColPin)}\" debe ser solo dígitos (el teclado del reloj es numérico), o estar vacío.");
                continue;
            }

            if (!TryParseOptionalScheduleTime(Get(ColScheduledStartTime), out var scheduledStartTime))
            {
                errors.Add($"Línea {lineNumber}: \"Hora Entrada\" \"{Get(ColScheduledStartTime)}\" no es una hora válida (ej.: \"8:00 AM\" o \"08:00\"), o vacío.");
                continue;
            }

            if (!TryParseOptionalScheduleTime(Get(ColScheduledEndTime), out var scheduledEndTime))
            {
                errors.Add($"Línea {lineNumber}: \"Hora Salida\" \"{Get(ColScheduledEndTime)}\" no es una hora válida (ej.: \"4:00 PM\" o \"16:00\"), o vacío.");
                continue;
            }

            if ((scheduledStartTime is null) != (scheduledEndTime is null))
            {
                errors.Add($"Línea {lineNumber}: captura ambas horas del horario (\"Hora Entrada\" y \"Hora Salida\"), o déjalas las dos vacías.");
                continue;
            }

            var alerts = new List<string>();
            if (weeklySalary is null)
            {
                alerts.Add("Sueldo semanal pendiente de captura.");
            }

            rows.Add(new EmployeeCatalogRow(
                lineNumber, number, fullName, area, position, hireDate, status,
                weeklySalary, overtimeHourlyRate, notes, pin, department,
                scheduledStartTime, scheduledEndTime, alerts));
        }

        return new EmployeeCatalogParseResult(rows, errors);
    }

    /// <summary>Nombre de columna (recortado, sin distinguir mayúsculas) → posición en el
    /// archivo. Una columna vacía al final (típico al guardar un CSV desde Excel, ver el
    /// archivo real que motivó esto) se ignora en vez de contar como columna "desconocida" —
    /// simplemente no aparece en el índice. Si el encabezado repitiera un nombre por error,
    /// gana la primera aparición.</summary>
    private static Dictionary<string, int> BuildColumnIndex(IReadOnlyList<string> headerFields)
    {
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerFields.Count; i++)
        {
            var name = headerFields[i].Trim();
            if (name.Length == 0)
            {
                continue;
            }
            index.TryAdd(name, i);
        }
        return index;
    }

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

    // Se acepta 12 horas con AM/PM (formato típico al exportar desde Excel/Google Sheets,
    // p. ej. "8:00 AM") Y 24 horas ("08:00", igual que el resto de la app — ver
    // AddEmployeeDialog/EditEmployeeDialog) — no hay ambigüedad entre los dos formatos como
    // sí la hay con las fechas (TryParseOptionalHireDate), así que no hace falta elegir uno
    // solo.
    private static readonly string[] ScheduleTimeFormats = ["h:mm tt", "hh:mm tt", "H:mm", "HH:mm"];

    /// <summary>Vacío → <c>null</c> ("horario no capturado"). El llamador es responsable de
    /// exigir que Hora Entrada y Hora Salida vengan juntas — aquí solo se valida el formato
    /// de una hora a la vez.</summary>
    private static bool TryParseOptionalScheduleTime(string raw, out TimeOnly? value)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
        {
            value = null;
            return true;
        }

        if (TimeOnly.TryParseExact(trimmed, ScheduleTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
