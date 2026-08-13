using System.IO;
using System.Text.Json;
using System.Windows;

namespace RelojChecador.WPF.Services;

// Nota: "Application.Current"/"ResourceDictionary" se referencian totalmente calificados
// como "System.Windows.X" donde hace falta en este archivo — el proyecto
// "RelojChecador.Application" (capa de casos de uso) comparte el espacio de nombres raíz
// "RelojChecador" con este proyecto WPF, así que "Application" sin calificar resuelve al
// namespace RelojChecador.Application en vez del tipo de WPF (mismo problema ya documentado
// en App.xaml.cs).

/// <summary>
/// Aplica y recuerda la preferencia de modo claro/oscuro. El tema se implementa
/// intercambiando el PRIMER ResourceDictionary de Application.Resources.MergedDictionaries
/// (Colors.Light.xaml o Colors.Dark.xaml, ver App.xaml) — el segundo (Styles.xaml) se
/// queda fijo, porque todos sus Setter usan DynamicResource sobre las claves de color, así
/// que se repintan solos en cuanto el primer diccionario cambia, sin reconstruir ninguna
/// ventana.
///
/// La preferencia se guarda en un archivo JSON propio (no en appsettings.*, que son de
/// solo configuración de infraestructura) para sobrevivir a reinstalaciones — mismo
/// criterio que la base SQLite local y los logs (ver installer/RelojChecador.iss).
/// </summary>
public sealed class ThemeService
{
    // Pack URI "component" — la forma robusta de cargar un ResourceDictionary por ruta
    // dentro del mismo ensamblado sin depender del contexto/hilo desde el que se llame.
    private const string LightDictionaryUri = "/RelojChecador.WPF;component/Themes/Colors.Light.xaml";
    private const string DarkDictionaryUri = "/RelojChecador.WPF;component/Themes/Colors.Dark.xaml";

    private readonly string _preferencesFilePath;

    public bool IsDarkMode { get; private set; }

    public ThemeService(string appDataDirectory)
    {
        _preferencesFilePath = Path.Combine(appDataDirectory, "preferences.json");
    }

    /// <summary>Se llama una sola vez al arrancar, antes de mostrar la primera ventana —
    /// lee la preferencia guardada (si existe) y la aplica.</summary>
    public void Initialize()
    {
        IsDarkMode = TryReadSavedPreference();
        ApplyColorDictionary(IsDarkMode);
    }

    public void Toggle()
    {
        IsDarkMode = !IsDarkMode;
        ApplyColorDictionary(IsDarkMode);
        TrySavePreference(IsDarkMode);
    }

    private static void ApplyColorDictionary(bool darkMode)
    {
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var newDictionary = new ResourceDictionary
        {
            Source = new Uri(darkMode ? DarkDictionaryUri : LightDictionaryUri, UriKind.Relative),
        };

        // El diccionario de colores es siempre el primero (ver App.xaml) — se reemplaza
        // por completo en vez de mutar sus entradas una por una.
        if (dictionaries.Count > 0)
        {
            dictionaries[0] = newDictionary;
        }
        else
        {
            dictionaries.Add(newDictionary);
        }
    }

    private bool TryReadSavedPreference()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return false;
            }

            var json = File.ReadAllText(_preferencesFilePath);
            var preferences = JsonSerializer.Deserialize<StoredPreferences>(json);
            return preferences?.DarkMode ?? false;
        }
        catch
        {
            // Un archivo corrupto o ilegible no debe impedir que la app arranque — se
            // arranca en modo claro (el valor por defecto) y ya.
            return false;
        }
    }

    private void TrySavePreference(bool darkMode)
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(new StoredPreferences(darkMode));
            File.WriteAllText(_preferencesFilePath, json);
        }
        catch
        {
            // No poder guardar la preferencia no es motivo para revertir el cambio visual
            // — en el peor caso, la próxima vez que abra la app vuelve a arrancar en claro.
        }
    }

    private sealed record StoredPreferences(bool DarkMode);
}
