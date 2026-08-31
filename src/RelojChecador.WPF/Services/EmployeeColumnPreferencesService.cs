using System.IO;
using System.Text.Json;

namespace RelojChecador.WPF.Services;

/// <summary>Una columna guardada: su clave estable (ver EmployeesView.xaml, x:Name de cada
/// DataGridColumn) y si está visible. El ORDEN de la lista completa ES el orden guardado —
/// no hace falta un campo aparte.</summary>
public sealed record EmployeeColumnPreference(string Key, bool IsVisible);

/// <summary>
/// Recuerda qué columnas de Empleados quiere ver el usuario y en qué orden — pedido
/// explícito: "me gustaría tener un botón o una opción de yo escoger qué columnas quiero y
/// cómo las quiero acomodar". Mismo criterio que ThemeService: un archivo JSON propio (no
/// appsettings.*, que es de configuración de infraestructura) en %LocalAppData%\RelojChecador,
/// para sobrevivir a reinstalaciones.
/// </summary>
public sealed class EmployeeColumnPreferencesService(string appDataDirectory)
{
    private readonly string _preferencesFilePath = Path.Combine(appDataDirectory, "employees-columns.json");

    /// <summary>Null si nunca se guardó nada (primera vez) o el archivo no se pudo leer —
    /// en ambos casos el llamador se queda con el orden/visibilidad por defecto que ya
    /// trae el XAML, sin tratarlo como un error.</summary>
    public IReadOnlyList<EmployeeColumnPreference>? TryLoad()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return null;
            }

            var json = File.ReadAllText(_preferencesFilePath);
            return JsonSerializer.Deserialize<List<EmployeeColumnPreference>>(json);
        }
        catch
        {
            return null;
        }
    }

    public void TrySave(IReadOnlyList<EmployeeColumnPreference> preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_preferencesFilePath, JsonSerializer.Serialize(preferences));
        }
        catch
        {
            // No poder guardar no debe impedir que el cambio se vea AHORA en pantalla — en
            // el peor caso, la próxima vez que abra la app vuelve al orden por defecto.
        }
    }
}
