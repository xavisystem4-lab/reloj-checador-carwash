using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace RelojChecador.Infrastructure.Cloud;

/// <summary>
/// Cliente mínimo contra la API REST autogenerada de Supabase (PostgREST) — no se usa el
/// paquete oficial supabase-csharp a propósito: para lo que necesita este motor de
/// sincronización (upserts por lote, autenticado con la service_role key) un HttpClient
/// delgado es más fácil de auditar y no agrega una dependencia más al .exe self-contained.
/// </summary>
public sealed class SupabaseRestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly HttpClient _httpClient;

    public SupabaseRestClient(HttpClient httpClient, SupabaseSyncOptions options)
    {
        if (!options.IsConfigured)
        {
            throw new InvalidOperationException(
                "SupabaseRestClient no debe construirse sin Url/ServiceRoleKey configurados — " +
                "quien lo registra en DI es responsable de verificar SupabaseSyncOptions.IsConfigured antes.");
        }

        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri(options.Url!.TrimEnd('/') + "/rest/v1/");
        _httpClient.DefaultRequestHeaders.Add("apikey", options.ServiceRoleKey);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ServiceRoleKey);
    }

    /// <summary>Inserta o actualiza por lote, resolviendo conflictos por la llave primaria
    /// "id" — así la sincronización es idempotente: reenviar la misma fila (p. ej. tras un
    /// reintento de red) nunca duplica, solo sobreescribe con el mismo valor.</summary>
    public async Task UpsertBatchAsync<T>(string table, IReadOnlyCollection<T> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{table}?on_conflict=id")
        {
            Content = JsonContent.Create(rows, options: JsonOptions),
        };
        request.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase rechazó el upsert a '{table}' ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }
    }

    /// <summary>Lee filas de una tabla con un filtro PostgREST crudo (ej.
    /// "status=eq.pending&amp;order=requested_at_utc.asc&amp;limit=1") — usado por
    /// <see cref="RemoteSyncRequestCoordinator"/> para consultar solicitudes de
    /// sincronización remota pendientes. Deliberadamente simple (un solo table+query, sin
    /// builder): es el único consumidor de GET hoy, y un query crudo es más fácil de
    /// auditar que una capa de abstracción para un solo caso de uso.</summary>
    public async Task<List<T>> GetAsync<T>(string table, string query, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync($"{table}?{query}", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase rechazó el GET a '{table}' ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }

        return await response.Content.ReadFromJsonAsync<List<T>>(JsonOptions, cancellationToken) ?? [];
    }

    /// <summary>Borra las filas que cumplan el filtro (ej. "id=in.(&lt;uuid1&gt;,&lt;uuid2&gt;)")
    /// — usado por SupabaseSyncBackgroundService.TryDeleteAttendancesRemoteAsync para
    /// reflejar en el Dashboard un borrado que el administrador hizo en la app de escritorio
    /// (pedido explícito del usuario: "podemos borrar en el sistema y que también mande la
    /// señal al sitio web"). Única excepción deliberada al resto del motor de sincronización
    /// (solo empuja cambios, nunca borra) — ver el comentario de esa clase.</summary>
    public async Task DeleteAsync(string table, string filter, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{table}?{filter}");
        request.Headers.Add("Prefer", "return=minimal");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase rechazó el DELETE a '{table}' ({(int)response.StatusCode} {response.StatusCode}): {body}");
        }
    }

    /// <summary>Actualiza parcialmente las filas que cumplan el filtro (ej.
    /// "id=eq.&lt;uuid&gt;") con los campos de <paramref name="body"/> — el resto de
    /// columnas no se toca. Usado por <see cref="RemoteSyncRequestCoordinator"/> para
    /// avanzar el estado de una solicitud de sincronización remota.</summary>
    public async Task PatchAsync(string table, string filter, object body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{table}?{filter}")
        {
            Content = JsonContent.Create(body, options: JsonOptions),
        };
        request.Headers.Add("Prefer", "return=minimal");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Supabase rechazó el PATCH a '{table}' ({(int)response.StatusCode} {response.StatusCode}): {responseBody}");
        }
    }
}
