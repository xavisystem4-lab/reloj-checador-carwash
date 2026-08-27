using System.Text;

namespace RelojChecador.Application.Common;

/// <summary>Parseo CSV mínimo, compartido por todos los importadores de este proyecto (ver
/// RelojChecador.Application.Employees.EmployeeImportParser y
/// EmployeeCatalogReplaceParser) — separa por comas respetando comillas dobles (<c>""</c>
/// dentro de un campo entrecomillado es una comilla literal). No soporta saltos de línea
/// dentro de un campo — suficiente para archivos generados por esta misma app o exportados
/// desde Excel/Google Sheets. Extraído de EmployeeImportParser para no duplicar esta misma
/// lógica en cada importador nuevo.</summary>
public static class CsvLineParser
{
    public static string[] SplitLine(string line)
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
            else if (c == ',')
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
}
