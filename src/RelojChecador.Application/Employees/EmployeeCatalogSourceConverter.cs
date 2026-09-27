using System.Linq;
using RelojChecador.Application.Common;

namespace RelojChecador.Application.Employees;

/// <summary>
/// Normaliza un archivo de origen que NO trae ya el encabezado canónico de
/// <see cref="EmployeeCatalogReplaceParser"/> (<c>Number,FullName,Area,Position,HireDate,Status,
/// WeeklySalary,OvertimeHourlyRate,Notes,Pin,Department</c>) a ese mismo formato — pedido explícito del
/// usuario: "que en cuanto suba un CSV o Excel automáticamente lo convierta ... para que sea
/// compatible al momento de importarlo", para no tener que transformar el Excel a mano cada
/// vez (como pasó con "REGISTRO EJECUTIVO EMPLEADOS.xlsx" antes de existir esta clase).
///
/// De momento reconoce un único formato alterno: la hoja "Registro Empleados" del Excel
/// maestro del negocio (columnas <see cref="RegistroEmpleadosHeader"/>). Si en el futuro
/// aparece otro formato de origen, agregar otro <c>if (HeaderMatches(...))</c> aquí — a
/// propósito NO se intenta adivinar una correspondencia de columnas genérica por similitud de
/// nombres: mapear mal una columna metería datos equivocados en silencio (mismo criterio que
/// "nunca adivinar MM/dd/yyyy" en EmployeeCatalogReplaceParser).
///
/// Quién lee el archivo (CSV en texto plano, o celdas de un .xlsx) es responsabilidad de quien
/// llama a esto — <c>ReplaceEmployeeCatalogDialog</c> en RelojChecador.WPF — así esta clase
/// se queda en Application, sin depender de ninguna librería de Excel ni de WPF, y se puede
/// probar con datos de texto simples (ver EmployeeCatalogSourceConverterTests).
/// </summary>
public static class EmployeeCatalogSourceConverter
{
    private static readonly string[] CanonicalHeader =
        ["Number", "FullName", "Area", "Position", "HireDate", "Status", "WeeklySalary", "OvertimeHourlyRate", "Notes", "Pin", "Department"];

    /// <summary>Todo el mundo termina en esta única sucursal — pedido explícito del usuario
    /// tras consolidar varias sucursales de prueba/otras ubicaciones en una sola: "sí
    /// fusiónalos [Arabica Café, CrisaTec, Otro, Plaza Sabo son reales] pero en los reportes
    /// que se acomoden por sucursal". Sin esto, cada vez que se reimporta el Excel (que sí
    /// trae esas otras ubicaciones en "Lugar de trabajo") se volverían a crear esas
    /// sucursales, deshaciendo la consolidación — ver el Department calculado abajo para
    /// dónde queda el dato real de cada quien.</summary>
    private const string UnifiedAreaName = "CAR-WASH";

    /// <summary>Encabezado real de la hoja "Registro Empleados" del Excel maestro del
    /// negocio, SIN contar la primera columna (ver <see cref="RegistroEmpleadosFirstColumnAliases"/>
    /// — el usuario le cambió el nombre de "ID Empleado" a "PIN" en una revisión posterior
    /// del archivo, mismo dato, mismo significado). "Fecha de salida" y "Antigüedad (años)"
    /// se leen pero se ignoran al convertir: la baja ya la expresa "Estado" y la antigüedad
    /// se recalcula sola a partir de HireDate, igual que en el resto de la app.</summary>
    private static readonly string[] RegistroEmpleadosHeaderRest =
    [
        "Nombre completo", "Fecha de ingreso", "Estado", "Fecha de salida",
        "Antigüedad (años)", "Sexo", "Lugar de trabajo", "Posición", "Sueldo", "Fecha de nacimiento",
    ];

    /// <summary>Nombres que ha tenido la primera columna de "Registro Empleados" — el mismo
    /// dato (el ID/PIN del empleado) bajo distintas etiquetas según la versión del archivo.
    /// Agregar aquí cualquier otro alias nuevo que aparezca, en vez de reemplazar los que ya
    /// hay: un Excel viejo guardado por accidente no debería dejar de reconocerse.</summary>
    private static readonly string[] RegistroEmpleadosFirstColumnAliases = ["ID Empleado", "PIN"];

    /// <summary>True si el encabezado ya trae lo mínimo que <see cref="EmployeeCatalogReplaceParser"/>
    /// necesita tal cual (por nombre, no por posición exacta — ver
    /// <see cref="EmployeeCatalogReplaceParser.HasUsableHeader"/>) — en ese caso no hace
    /// falta convertir nada. Antes comparaba contra <see cref="CanonicalHeader"/> posición
    /// por posición, lo que rechazaba en falso cualquier variante válida del catálogo (p.
    /// ej. uno con "Hora Entrada"/"Hora Salida" en vez de Department, o con las columnas en
    /// otro orden) con el mismo error genérico de "formato no reconocido" — caso real que
    /// motivó este cambio.</summary>
    public static bool IsCanonicalHeader(IReadOnlyList<string> header) => EmployeeCatalogReplaceParser.HasUsableHeader(header);

    /// <summary>True si el encabezado es cualquiera de los formatos que esta clase sabe leer
    /// (el canónico, o alguno de los alternos como "Registro Empleados") — usado por quien
    /// busca la fila de encabezado dentro de un archivo (p. ej. ExcelCatalogReader, que tiene
    /// que saltarse título/instrucciones antes de llegar a la fila real de encabezados).</summary>
    public static bool IsRecognizedHeader(IReadOnlyList<string> header) =>
        IsCanonicalHeader(header) || IsRegistroEmpleadosHeader(header) || IsPinListHeader(header)
        || EmployeeCatalogGenericConverter.IsRecognizableHeader(header);

    /// <summary>Lista corta "ID Empleado, Nombre completo" — pedido explícito del usuario
    /// ("tengo un archivo CSV, quiero reemplazar ... pero el sistema dice que no es compatible").
    /// Es la lista de usuarios del reloj: el ID es a la vez el Número y el PIN.</summary>
    private static readonly string[] PinListFirstColumnAliases = ["ID Empleado", "ID", "PIN", "Número", "Numero", "No.", "No"];
    private static readonly string[] PinListNameColumnAliases = ["Nombre completo", "Nombre"];

    /// <summary>Catálogo canónico a partir de pares (PIN, nombre) — lo usan la lista corta de
    /// arriba y "Traer registros del reloj al sistema" (usuarios leídos del reloj). Número = PIN;
    /// lo demás vacío, que en el reemplazo significa "no tocar lo ya capturado".</summary>
    public static IReadOnlyList<string> FromPinList(IEnumerable<(string Pin, string FullName)> users)
    {
        var lines = new List<string> { string.Join(",", CanonicalHeader) };
        foreach (var (pin, fullName) in users)
        {
            var trimmedPin = pin.Trim();
            var trimmedName = fullName.Trim();
            if (trimmedPin.Length == 0 && trimmedName.Length == 0)
            {
                continue;
            }

            var fields = new[] { trimmedPin, trimmedName, UnifiedAreaName, "", "", "Activo", "", "", "", trimmedPin, "" };
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        return lines;
    }

    /// <summary>Intenta convertir <paramref name="header"/> + <paramref name="rows"/> (ya
    /// separados en celdas de texto, sin importar si vinieron de un CSV o de un .xlsx) al
    /// formato canónico. Cada celda de <paramref name="rows"/> ya debe venir como texto plano
    /// listo para el parser final (fechas en <c>yyyy-MM-dd</c>, números con punto decimal
    /// invariante) — la conversión de tipos de celda de Excel (fecha, numérico, texto) es
    /// responsabilidad de quien arma <paramref name="rows"/>, no de esta clase.</summary>
    public static bool TryConvert(
        IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string?>> rows,
        out IReadOnlyList<string> csvLines, out string? error) =>
        TryConvert(header, rows, out csvLines, out _, out error);

    /// <summary>Igual que la otra sobrecarga, más <paramref name="description"/>: qué formato se
    /// reconoció (y, en el formato libre, qué columna fue a dónde y cuáles se ignoraron) — para
    /// mostrarlo en la vista previa y que no sea una caja negra.</summary>
    public static bool TryConvert(
        IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string?>> rows,
        out IReadOnlyList<string> csvLines, out string? description, out string? error)
    {
        description = null;
        if (IsRegistroEmpleadosHeader(header))
        {
            csvLines = ConvertFromRegistroEmpleados(rows);
            description = "Formato reconocido: hoja \"Registro Empleados\" del Excel maestro.";
            error = null;
            return true;
        }

        if (IsPinListHeader(header))
        {
            csvLines = FromPinList(rows.Select(r => (Cell(r, 0), Cell(r, 1))));
            description = "Formato reconocido: lista de usuarios del reloj (PIN + nombre).";
            error = null;
            return true;
        }

        // Cualquier otra lista de empleados: se reconocen sus columnas por nombre (v1.70.0).
        if (EmployeeCatalogGenericConverter.TryConvert(header, rows, out csvLines, out description, out error))
        {
            return true;
        }

        csvLines = [];
        error = error ?? $"El archivo no coincide con ningún formato reconocido (su encabezado es: {string.Join(", ", header)}). " +
            "Formatos aceptados: el catálogo de reemplazo (Number,FullName,Area,...), la hoja \"Registro Empleados\" " +
            "del Excel maestro, o una lista de dos columnas \"ID Empleado, Nombre completo\".";
        return false;
    }

    private static IReadOnlyList<string> ConvertFromRegistroEmpleados(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var lines = new List<string> { string.Join(",", CanonicalHeader) };

        foreach (var row in rows)
        {
            var number = Cell(row, 0);
            var fullName = Cell(row, 1);
            if (number.Length == 0 && fullName.Length == 0)
            {
                continue; // fila en blanco (frecuente al final de la hoja del Excel)
            }

            var hireDate = Cell(row, 2);
            var status = string.Equals(Cell(row, 3), "Inactivo", StringComparison.OrdinalIgnoreCase) ? "Inactivo" : "Activo";
            // Cell(row, 4) = Fecha de salida, Cell(row, 5) = Antigüedad (años): ignoradas, ver comentario de clase.
            var sexo = Cell(row, 6);
            var originalArea = Cell(row, 7);
            var position = Cell(row, 8);
            var salary = Cell(row, 9);
            var birthDate = Cell(row, 10);

            // Area SIEMPRE es la única sucursal unificada — el "Lugar de trabajo" real del
            // Excel se conserva en Department, pero solo cuando de verdad aporta algo (si ya
            // era la misma sucursal unificada, guardarlo ahí sería puro ruido repetido).
            var area = UnifiedAreaName;
            var department = string.Equals(originalArea, UnifiedAreaName, StringComparison.OrdinalIgnoreCase)
                ? ""
                : originalArea;

            // Misma nota compuesta que ya se usaba al armar este catálogo a mano — "Sexo: X |
            // Fecha de nacimiento: Y" — para no perder esos dos datos, que el formato
            // canónico no tiene columna propia para ellos.
            var noteParts = new List<string>(2);
            if (sexo.Length > 0)
            {
                noteParts.Add($"Sexo: {sexo}");
            }
            if (birthDate.Length > 0)
            {
                noteParts.Add($"Fecha de nacimiento: {birthDate}");
            }
            var notes = string.Join(" | ", noteParts);

            // Pin = mismo número que Number: convención ya establecida a mano para este
            // negocio (el "ID Empleado" del Excel es el mismo número que se usa como PIN en
            // el reloj checador) — pedido explícito del usuario al restructurar el catálogo:
            // "quiero que se respete tal cual la numeración del PIN ... en ese orden".
            var fields = new[] { number, fullName, area, position, hireDate, status, salary, "", notes, number, department };
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        return lines;
    }

    private static string Cell(IReadOnlyList<string?> row, int index) =>
        (index < row.Count ? row[index] : null)?.Trim() ?? "";

    private static bool IsRegistroEmpleadosHeader(IReadOnlyList<string> header) =>
        header.Count == RegistroEmpleadosHeaderRest.Length + 1
        && RegistroEmpleadosFirstColumnAliases.Contains(header[0].Trim(), StringComparer.OrdinalIgnoreCase)
        && HeaderMatches(header.Skip(1).ToArray(), RegistroEmpleadosHeaderRest);

    /// <summary>Dos columnas con nombre (las vacías al final, típicas de Excel, no cuentan).</summary>
    private static bool IsPinListHeader(IReadOnlyList<string> header)
    {
        var named = header.Select(h => h.Trim()).ToList();
        while (named.Count > 0 && named[^1].Length == 0)
        {
            named.RemoveAt(named.Count - 1);
        }

        return named.Count == 2
            && PinListFirstColumnAliases.Contains(named[0], StringComparer.OrdinalIgnoreCase)
            && PinListNameColumnAliases.Contains(named[1], StringComparer.OrdinalIgnoreCase);
    }

    private static bool HeaderMatches(IReadOnlyList<string> header, string[] expected) =>
        header.Count == expected.Length
        && header.Select(h => h.Trim()).SequenceEqual(expected, StringComparer.OrdinalIgnoreCase);

    private static string CsvEscape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
