using System.Text.Json;
using System.Text.Json.Nodes;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Escribe la <c>service_role</c> key de Supabase en <c>%LocalAppData%\RelojChecador\
/// appsettings.Local.json</c> — antes esto era un paso 100% manual (ver
/// RelojChecador.Infrastructure.Cloud/README.md, "Cómo activarla en una instalación"): crear
/// el archivo a mano con un editor de texto y reiniciar la app. Pedido explícito del
/// usuario: que el botón "Conectar con nube" (ver UpdateViewModel) pueda hacer el enlace él
/// mismo la primera vez, sin editar ningún archivo a mano ni reiniciar.
///
/// Preserva cualquier otro contenido que ya tuviera el archivo (nunca lo sobrescribe entero)
/// — alguien pudo haber agregado otra clave bajo "Supabase" u otra sección aparte a mano
/// antes de que existiera este botón.
/// </summary>
public sealed class SupabaseLocalConfigStore(string filePath)
{
    /// <summary>Guarda la clave en disco Y la aplica de inmediato sobre
    /// <paramref name="options"/> (la misma instancia Singleton que ya usa el resto de la
    /// app) — así la sincronización queda activa en esta sesión sin reiniciar, no solo la
    /// próxima vez que abra la app.</summary>
    public async Task SaveServiceRoleKeyAsync(
        string serviceRoleKey, SupabaseSyncOptions options, CancellationToken cancellationToken = default)
    {
        var trimmed = serviceRoleKey.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("La clave no puede estar vacía.", nameof(serviceRoleKey));
        }

        JsonNode root = new JsonObject();
        if (File.Exists(filePath))
        {
            var existingText = await File.ReadAllTextAsync(filePath, cancellationToken);
            // Un archivo vacío o corrupto no debe impedir guardar la clave nueva — se
            // reemplaza por un documento en blanco en vez de reventar (el usuario está
            // intentando ARREGLAR la configuración, no se le puede negar por un archivo
            // previo dañado). JsonNode.Parse lanza JsonException con JSON inválido, no
            // devuelve null — de ahí el try/catch aparte del "?? new JsonObject()" de abajo,
            // que solo cubre el caso de un JSON válido pero que no es un objeto (un array,
            // un número suelto, etc.).
            if (!string.IsNullOrWhiteSpace(existingText))
            {
                try
                {
                    root = JsonNode.Parse(existingText) as JsonObject ?? new JsonObject();
                }
                catch (System.Text.Json.JsonException)
                {
                    root = new JsonObject();
                }
            }
        }

        var rootObject = (JsonObject)root;
        if (rootObject["Supabase"] is not JsonObject supabaseSection)
        {
            supabaseSection = new JsonObject();
            rootObject["Supabase"] = supabaseSection;
        }

        supabaseSection["ServiceRoleKey"] = trimmed;

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = rootObject.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(filePath, json, cancellationToken);

        // Aplicar en memoria AL FINAL, solo tras guardar en disco con éxito — si escribir
        // el archivo falla, la sesión actual se queda como estaba (no "medio enlazada").
        options.ServiceRoleKey = trimmed;
    }
}
