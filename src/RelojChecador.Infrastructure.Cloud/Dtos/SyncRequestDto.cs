namespace RelojChecador.Infrastructure.Cloud.Dtos;

/// <summary>
/// Espejo de una fila de public.sync_requests (ver
/// supabase/migrations/20260814060000_add_sync_requests.sql). A diferencia del resto de
/// los DTOs de este archivo hermano (SyncDtos.cs), este NO tiene FromDomain — la solicitud
/// de sincronización remota vive únicamente en la nube, no espeja ninguna entidad del
/// dominio local. PascalCase a propósito, igual que el resto: SupabaseRestClient serializa/
/// deserializa con JsonNamingPolicy.SnakeCaseLower.
/// </summary>
public sealed record SyncRequestDto(
    Guid Id,
    string Status,
    string? RequestedByEmail,
    DateTime RequestedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? ResultSummary,
    string? ErrorMessage);
