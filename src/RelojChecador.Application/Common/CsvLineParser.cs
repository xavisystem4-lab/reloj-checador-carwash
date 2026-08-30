using System.Text;

namespace RelojChecador.Application.Common;

/// <summary>Parseo CSV mínimo, compartido por todos los importadores de este proyecto (ver
/// RelojChecador.Application.Employees.EmployeeImportParser y
/// EmployeeCatalogReplaceParser) — separa por el delimitador dado (coma por defecto)
/// respetando comillas dobles (<c>""</c> dentro de un campo entrecomillado es una comilla
/// literal). No soporta saltos de línea dentro de un campo — suficiente para archivos
/// generados por esta misma app o exportados desde Excel/Google Sheets. Extraído de
/// EmployeeImportParser para no duplicar esta misma lógica en cada importador nuevo.</summary>
public static class CsvLineParser
{
    public static string[] SplitLine(string line, char delimiter = ',')
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return [.. fields];
    }

    /// <summary>Detecta si el archivo viene separado por coma o por punto y coma, contando
    /// ambos caracteres en <paramref name="headerLine"/> (la primera línea) y usando el que
    /// aparece más. Caso real que motivó esto: Excel con configuración regional en español
    /// (México y la mayoría de Latinoamérica) usa la coma como separador DECIMAL, así que al
    /// hacer "Guardar como" CSV exporta las columnas separadas por punto y coma en vez de
    /// coma — comportamiento documentado de Excel, no un archivo mal armado. Si no hay
    /// ninguno de los dos caracteres, o hay el mismo número, se asume coma (el formato de
    /// toda la vida de este proyecto, y el que generan sus propios "Exportar plantilla" /
    /// exportes CSV).</summary>
    public static char DetectDelimiter(string headerLine)
    {
        var commas = headerLine.Count(c => c == ',');
        var semicolons = headerLine.Count(c => c == ';');
        return semicolons > commas ? ';' : ',';
    }
}
