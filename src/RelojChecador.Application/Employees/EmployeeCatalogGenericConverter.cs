using System.Globalization;
using System.Linq;
using System.Text;

namespace RelojChecador.Application.Employees;

/// <summary>
/// Reconoce "cualquier" lista de empleados (CSV o Excel) y la adapta al catálogo canónico de
/// <see cref="EmployeeCatalogReplaceParser"/> — pedido explícito del usuario (v1.70.0): "si
/// importo un archivo, que rápidamente lo reconozca y lo adapte para poderlo reemplazar,
/// automáticamente", sin tener que acomodar las columnas a mano.
///
/// Cómo reconoce las columnas: por NOMBRE de encabezado contra una lista cerrada de sinónimos
/// (español e inglés, sin distinguir mayúsculas, acentos ni signos: "No. Empleado",
/// "Nombre(s)", "Apellido Paterno", "Puesto", "Sueldo semanal", "Fecha de ingreso", ...).
/// A propósito NO adivina por parecido ni por el contenido de las celdas: una columna que no
/// está en la lista simplemente se ignora (y se avisa cuáles fueron), nunca se mete en otro
/// campo — el mismo criterio de "no guardar un dato equivocado en silencio" del resto del
/// flujo de reemplazo. Si aparece un encabezado nuevo que debería reconocerse, se agrega su
/// sinónimo en <see cref="Synonyms"/>.
///
/// Lo único obligatorio es poder armar el NOMBRE: una columna de nombre completo, o nombre +
/// apellido(s) por separado (se juntan en "Nombre Paterno Materno"). Todo lo demás es opcional:
/// <list type="bullet">
/// <item>Número: si no viene pero sí el PIN, Número = PIN (convención del negocio). Si no viene
/// ninguno, se deja VACÍO y lo completa <c>EmployeesViewModel.PrepareCatalogReplacePreviewAsync</c>
/// (conserva el número de quien ya existe con ese nombre, o asigna el siguiente libre).</item>
/// <item>Sucursal: si no viene, la única sucursal del negocio (CAR-WASH); si viene otra
/// ubicación se guarda en Department, igual que la hoja "Registro Empleados".</item>
/// <item>Estado: "Activo"/"Alta" → Activo; "Inactivo"/"Baja" → Inactivo.</item>
/// <item>Fechas: se aceptan ISO, DD/MM/AAAA (o con guiones/puntos) y número de serie de Excel.</item>
/// <item>Sueldos: se quitan "$", espacios y separadores de miles.</item>
/// <item>Sexo, fecha de nacimiento, teléfono, correo, RFC, CURP, NSS: no tienen columna propia en
/// el catálogo, así que se conservan en Notas ("Sexo: M | Tel: ...").</item>
/// <item>Nombres escritos TODO EN MAYÚSCULAS se pasan a "Nombre Propio" (así se guardan los
/// demás); "APELLIDOS, NOMBRE" con coma se voltea a "Nombre Apellidos".</item>
/// </list>
/// </summary>
public static class EmployeeCatalogGenericConverter
{
    private const string UnifiedAreaName = "CAR-WASH";

    private enum Field
    {
        Number, Pin, FullName, FirstName, LastName, PaternalLastName, MaternalLastName, Area, Position,
        Department, HireDate, Status, WeeklySalary, OvertimeHourlyRate, Notes, StartTime, EndTime,
        SpecialSchedule, Sex, BirthDate, Phone, Email, Rfc, Curp, Nss,
    }

    /// <summary>Sinónimos ya normalizados (minúsculas, sin acentos ni signos, espacios simples).
    /// Ver <see cref="NormalizeHeader"/>.</summary>
    private static readonly Dictionary<Field, string[]> Synonyms = new()
    {
        [Field.Number] =
        [
            "number", "numero", "num", "no", "n", "numero de empleado", "numero empleado", "no empleado",
            "no de empleado", "num empleado", "num de empleado", "clave", "clave empleado", "clave de empleado",
            "codigo", "codigo empleado", "id", "id empleado", "employee id", "employee number", "nomina",
            "numero de nomina", "no nomina", "folio",
        ],
        [Field.Pin] =
        [
            "pin", "pin reloj", "pin del reloj", "id reloj", "id del reloj", "id checador", "user id", "userid",
            "id usuario", "id de usuario", "no reloj", "numero reloj", "numero en reloj",
        ],
        [Field.FullName] =
        [
            "fullname", "full name", "nombre completo", "nombre del empleado", "nombre empleado", "empleado",
            "trabajador", "colaborador", "name", "empleados", "nombre y apellidos", "nombre y apellido",
            "apellidos y nombre", "apellidos y nombres", "persona",
        ],
        [Field.FirstName] = ["nombre", "nombres", "nombre s", "first name", "firstname", "primer nombre"],
        [Field.LastName] = ["apellido", "apellidos", "last name", "lastname", "surname"],
        [Field.PaternalLastName] = ["apellido paterno", "primer apellido", "paterno", "ap paterno", "a paterno"],
        [Field.MaternalLastName] = ["apellido materno", "segundo apellido", "materno", "ap materno", "a materno"],
        [Field.Area] = ["area", "sucursal", "branch", "tienda", "lugar de trabajo", "ubicacion", "centro de trabajo", "planta"],
        [Field.Position] = ["position", "puesto", "posicion", "cargo", "funcion", "ocupacion", "rol", "job title"],
        [Field.Department] = ["department", "departamento", "depto", "dept"],
        [Field.HireDate] =
        [
            "hiredate", "hire date", "fecha de ingreso", "fecha ingreso", "ingreso", "fecha de alta", "fecha alta",
            "alta", "fecha de contratacion", "fecha contratacion", "inicio", "fecha de inicio",
        ],
        [Field.Status] = ["status", "estado", "estatus", "situacion", "activo"],
        [Field.WeeklySalary] =
        [
            "weeklysalary", "weekly salary", "sueldo", "sueldo semanal", "salario", "salario semanal", "pago",
            "pago semanal", "semanal", "sueldo base",
        ],
        [Field.OvertimeHourlyRate] =
        [
            "overtimehourlyrate", "hora extra", "horas extra", "pago hora extra", "precio hora extra",
            "tarifa hora extra", "costo hora extra",
        ],
        [Field.Notes] = ["notes", "notas", "nota", "observaciones", "observacion", "comentarios", "comentario"],
        [Field.StartTime] = ["hora entrada", "hora de entrada", "entrada"],
        [Field.EndTime] = ["hora salida", "hora de salida", "salida"],
        [Field.SpecialSchedule] = ["horario especial"],
        [Field.Sex] = ["sexo", "genero", "sex", "gender"],
        [Field.BirthDate] = ["fecha de nacimiento", "fecha nacimiento", "nacimiento", "birthdate", "birth date", "cumpleanos"],
        [Field.Phone] = ["telefono", "tel", "celular", "phone", "movil"],
        [Field.Email] = ["correo", "email", "e mail", "correo electronico", "mail"],
        [Field.Rfc] = ["rfc"],
        [Field.Curp] = ["curp"],
        [Field.Nss] = ["nss", "imss", "numero de seguro social", "seguro social"],
    };

    private static readonly Dictionary<string, Field> FieldBySynonym = BuildSynonymIndex();

    private static readonly string[] CanonicalHeader =
    [
        "Number", "FullName", "Area", "Position", "HireDate", "Status", "WeeklySalary", "OvertimeHourlyRate",
        "Notes", "Pin", "Department", "Hora Entrada", "Hora Salida", "Horario Especial",
    ];

    /// <summary>True si el encabezado trae, reconocidas por sinónimo, las columnas para armar el
    /// nombre — y al menos otra columna reconocida (para no confundir cualquier fila de texto
    /// con un encabezado al buscarlo dentro de un Excel con título).</summary>
    public static bool IsRecognizableHeader(IReadOnlyList<string> header)
    {
        var map = MapColumns(header, out _);
        return HasName(map) && map.Count >= 2;
    }

    /// <summary>Convierte a CSV canónico. <paramref name="description"/> resume qué columnas se
    /// reconocieron y cuáles se ignoraron, para mostrarlo en la vista previa.</summary>
    public static bool TryConvert(
        IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string?>> rows,
        out IReadOnlyList<string> csvLines, out string? description, out string? error)
    {
        var map = MapColumns(header, out var ignored);
        if (!HasName(map))
        {
            csvLines = [];
            description = null;
            error = "No se encontró una columna con el nombre del empleado (p. ej. \"Nombre\", \"Nombre completo\", " +
                $"\"Empleado\", o \"Nombre\" + \"Apellidos\"). Encabezado del archivo: {string.Join(", ", header.Where(h => !string.IsNullOrWhiteSpace(h)))}.";
            return false;
        }

        var lines = new List<string> { string.Join(",", CanonicalHeader) };
        foreach (var row in rows)
        {
            string Get(Field field) =>
                map.TryGetValue(field, out var index) && index < row.Count ? (row[index] ?? "").Trim() : "";

            var fullName = BuildFullName(Get(Field.FullName), Get(Field.FirstName), Get(Field.LastName),
                Get(Field.PaternalLastName), Get(Field.MaternalLastName));
            var pin = DigitsOnlyOrEmpty(Get(Field.Pin));
            var number = CleanNumber(Get(Field.Number));
            if (fullName.Length == 0)
            {
                continue; // fila en blanco, subtotal o renglón de firmas al final de la hoja
            }

            if (number.Length == 0)
            {
                number = pin;
            }
            if (pin.Length == 0 && !map.ContainsKey(Field.Pin) && number.Length > 0 && number.All(char.IsAsciiDigit))
            {
                pin = number; // Número = PIN, convención del negocio (igual que "Registro Empleados")
            }

            var originalArea = Get(Field.Area);
            var department = Get(Field.Department);
            if (department.Length == 0 && originalArea.Length > 0 &&
                !string.Equals(originalArea, UnifiedAreaName, StringComparison.OrdinalIgnoreCase))
            {
                department = originalArea;
            }

            var notes = new List<string>();
            AddNote(notes, "", Get(Field.Notes));
            AddNote(notes, "Sexo: ", Get(Field.Sex));
            AddNote(notes, "Fecha de nacimiento: ", NormalizeDate(Get(Field.BirthDate)));
            AddNote(notes, "Tel: ", Get(Field.Phone));
            AddNote(notes, "Correo: ", Get(Field.Email));
            AddNote(notes, "RFC: ", Get(Field.Rfc));
            AddNote(notes, "CURP: ", Get(Field.Curp));
            AddNote(notes, "NSS: ", Get(Field.Nss));

            var startTime = Get(Field.StartTime);
            var endTime = Get(Field.EndTime);
            if (startTime.Length == 0 || endTime.Length == 0)
            {
                startTime = endTime = ""; // el catálogo exige las dos o ninguna
            }

            var fields = new[]
            {
                number, fullName, UnifiedAreaName, Get(Field.Position), NormalizeDate(Get(Field.HireDate)),
                NormalizeStatus(Get(Field.Status)), CleanMoney(Get(Field.WeeklySalary)),
                CleanMoney(Get(Field.OvertimeHourlyRate)), string.Join(" | ", notes), pin, department,
                startTime, endTime, NormalizeYesNo(Get(Field.SpecialSchedule)),
            };
            lines.Add(string.Join(",", fields.Select(CsvEscape)));
        }

        var recognized = map
            .OrderBy(kv => kv.Value)
            .Select(kv => $"{header[kv.Value].Trim()} → {Describe(kv.Key)}");
        description = "Formato reconocido automáticamente: " + string.Join(", ", recognized) + "." +
            (ignored.Count > 0 ? $" Columnas ignoradas: {string.Join(", ", ignored)}." : "");
        csvLines = lines;
        error = null;
        return true;
    }

    private static Dictionary<Field, int> MapColumns(IReadOnlyList<string> header, out List<string> ignored)
    {
        var map = new Dictionary<Field, int>();
        ignored = [];
        for (var i = 0; i < header.Count; i++)
        {
            var raw = header[i]?.Trim() ?? "";
            if (raw.Length == 0)
            {
                continue;
            }

            if (FieldBySynonym.TryGetValue(NormalizeHeader(raw), out var field) && map.TryAdd(field, i))
            {
                continue;
            }

            // "Antigüedad", "Fecha de salida", totales, etc. — no tienen a dónde ir.
            ignored.Add(raw);
        }

        // "Nombre" solo (sin columnas de apellido) ES el nombre completo.
        var hasLastNames = map.ContainsKey(Field.LastName) || map.ContainsKey(Field.PaternalLastName) || map.ContainsKey(Field.MaternalLastName);
        if (!map.ContainsKey(Field.FullName) && !hasLastNames && map.Remove(Field.FirstName, out var nameIndex))
        {
            map[Field.FullName] = nameIndex;
        }

        return map;
    }

    private static bool HasName(Dictionary<Field, int> map) =>
        map.ContainsKey(Field.FullName) || map.ContainsKey(Field.FirstName);

    private static Dictionary<string, Field> BuildSynonymIndex()
    {
        var index = new Dictionary<string, Field>(StringComparer.Ordinal);
        foreach (var (field, synonyms) in Synonyms)
        {
            foreach (var synonym in synonyms)
            {
                index.TryAdd(NormalizeHeader(synonym), field);
            }
        }
        return index;
    }

    /// <summary>Minúsculas, sin acentos, cualquier signo (".", "#", "(", ":", "_", "/") como
    /// espacio, espacios colapsados. "No. Empleado" → "no empleado"; "Nombre(s)" → "nombre s".</summary>
    public static string NormalizeHeader(string value)
    {
        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string BuildFullName(string fullName, string firstName, string lastName, string paternal, string maternal)
    {
        string name;
        if (fullName.Length > 0)
        {
            // "URIBE GARCIA, ADRIAN" → "ADRIAN URIBE GARCIA"
            var comma = fullName.IndexOf(',');
            name = comma > 0 && comma < fullName.Length - 1
                ? $"{fullName[(comma + 1)..].Trim()} {fullName[..comma].Trim()}"
                : fullName;
        }
        else
        {
            name = string.Join(' ', new[] { firstName, lastName, paternal, maternal }.Where(p => p.Length > 0));
        }

        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return IsAllUpper(name) ? CultureInfo.GetCultureInfo("es-MX").TextInfo.ToTitleCase(name.ToLowerInvariant()) : name;
    }

    private static bool IsAllUpper(string value) =>
        value.Any(char.IsLetter) && !value.Any(char.IsLower);

    private static string CleanNumber(string value)
    {
        var trimmed = value.Trim();
        // Excel entrega "12.0" o "12,0" cuando la celda es numérica.
        if (decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) && n == decimal.Truncate(n) && n >= 0 &&
            trimmed.All(c => char.IsAsciiDigit(c) || c == '.' || c == ','))
        {
            return ((long)n).ToString(CultureInfo.InvariantCulture);
        }
        return trimmed;
    }

    private static string DigitsOnlyOrEmpty(string value)
    {
        var cleaned = CleanNumber(value);
        return cleaned.Length > 0 && cleaned.All(char.IsAsciiDigit) ? cleaned : "";
    }

    private static string CleanMoney(string value)
    {
        var cleaned = value.Replace("$", "").Replace("MXN", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "").Trim();
        if (cleaned.Length == 0)
        {
            return "";
        }

        // "1,500.00" → 1500.00 ; "1.500,00" → 1500.00 ; "1500,5" → 1500.5
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');
        if (lastComma >= 0 && lastDot >= 0)
        {
            cleaned = lastComma > lastDot
                ? cleaned.Replace(".", "").Replace(',', '.')
                : cleaned.Replace(",", "");
        }
        else if (lastComma >= 0)
        {
            cleaned = cleaned.Length - lastComma - 1 == 3 ? cleaned.Replace(",", "") : cleaned.Replace(',', '.');
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString(CultureInfo.InvariantCulture)
            : value.Trim(); // que el parser avise en la vista previa, no adivinar
    }

    private static readonly string[] DayFirstFormats =
    [
        "d/M/yyyy", "d-M-yyyy", "d.M.yyyy", "d/M/yy", "d-M-yy", "yyyy-M-d", "yyyy/M/d",
        "d/M/yyyy H:mm", "d/M/yyyy H:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss",
    ];

    /// <summary>A <c>yyyy-MM-dd</c> cuando no hay ambigüedad: día primero (México), o número de
    /// serie de Excel. Si no se entiende, se deja tal cual para que la vista previa lo marque.</summary>
    public static string NormalizeDate(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return "";
        }

        if (DateTime.TryParseExact(trimmed, DayFirstFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 1 and < 80000)
        {
            return DateTime.FromOADate(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return trimmed;
    }

    private static string NormalizeStatus(string value) =>
        NormalizeHeader(value) switch
        {
            "" => "",
            "inactivo" or "inactiva" or "inactive" or "baja" or "de baja" or "dado de baja" or "no" or "0" or "suspendido" => "Inactivo",
            _ => "Activo",
        };

    private static string NormalizeYesNo(string value) =>
        NormalizeHeader(value) switch
        {
            "si" or "s" or "yes" or "x" or "1" or "verdadero" or "true" => "Sí",
            "no" or "n" or "0" or "falso" or "false" => "No",
            _ => "",
        };

    private static void AddNote(List<string> notes, string label, string value)
    {
        if (value.Length > 0)
        {
            notes.Add(label + value);
        }
    }

    private static string Describe(Field field) => field switch
    {
        Field.Number => "Número",
        Field.Pin => "PIN",
        Field.FullName => "Nombre completo",
        Field.FirstName => "Nombre",
        Field.LastName => "Apellidos",
        Field.PaternalLastName => "Apellido paterno",
        Field.MaternalLastName => "Apellido materno",
        Field.Area => "Sucursal",
        Field.Position => "Puesto",
        Field.Department => "Departamento",
        Field.HireDate => "Fecha de ingreso",
        Field.Status => "Estado",
        Field.WeeklySalary => "Sueldo semanal",
        Field.OvertimeHourlyRate => "Hora extra",
        Field.StartTime => "Hora entrada",
        Field.EndTime => "Hora salida",
        Field.SpecialSchedule => "Horario especial",
        _ => "Notas",
    };

    private static string CsvEscape(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}
