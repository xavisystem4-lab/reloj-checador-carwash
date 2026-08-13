namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Configuración de la sincronización con Supabase. <see cref="Url"/> y
/// <see cref="AnonKey"/> son seguros de commitear (están pensados para ser públicos,
/// protegidos por RLS) — ver src/RelojChecador.WPF/appsettings.json.
/// <see cref="ServiceRoleKey"/> NUNCA se commitea: se lee de
/// %LocalAppData%\RelojChecador\appsettings.Local.json, un archivo que cada instalación
/// crea localmente y que nunca sale de la máquina (ver README de Infrastructure.Cloud).
/// </summary>
public sealed class SupabaseSyncOptions
{
    public string? Url { get; set; }
    public string? AnonKey { get; set; }
    public string? ServiceRoleKey { get; set; }
    public int IntervalSeconds { get; set; } = 60;

    /// <summary>Sin URL o sin la clave de escritura no hay nada que sincronizar — la app
    /// sigue funcionando 100% local (ver README, "operación offline-first"), simplemente
    /// sin nube todavía. Nunca debe ser un error duro que impida arrancar.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url) && !string.IsNullOrWhiteSpace(ServiceRoleKey);
}
