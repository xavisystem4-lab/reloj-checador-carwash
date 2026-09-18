using System.Net;
using System.Text.Json;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Error devuelto por la API REST de Supabase. Subclase de <see cref="HttpRequestException"/>
/// (todo el que ya atrapaba esa excepción sigue funcionando) que además expone el código de
/// error de Postgres que PostgREST incluye en el cuerpo — el motor de sincronización lo usa
/// para distinguir un choque de llave única (23505), que sí sabe reparar, de cualquier otro
/// fallo, sin tener que buscar texto dentro del mensaje.
/// </summary>
public sealed class SupabaseApiException(string message, HttpStatusCode statusCode, string? postgresCode)
    : HttpRequestException(message, inner: null, statusCode)
{
    /// <summary>SQLSTATE de Postgres (p. ej. "23505" llave única duplicada, "23503" llave
    /// foránea), o null si el cuerpo no traía uno reconocible.</summary>
    public string? PostgresCode { get; } = postgresCode;

    public bool IsUniqueViolation => PostgresCode == "23505";

    /// <summary>Extrae "code" de un cuerpo de error de PostgREST, p. ej.
    /// <c>{"code":"23505","details":"...","message":"..."}</c>; null si no es JSON o no lo trae.</summary>
    public static string? ParsePostgresCode(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("code", out var code)
                && code.ValueKind == JsonValueKind.String
                    ? code.GetString()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
