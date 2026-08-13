namespace RelojChecador.Application.Updates;

/// <summary>Resultado de consultar la última versión publicada. Se calcula siempre
/// (incluso si no hay versión más nueva) para poder mostrarle al usuario "ya tienes la
/// versión más reciente" en vez de solo un silencio — nunca se asume éxito sin decir
/// explícitamente qué se encontró.</summary>
public sealed record UpdateCheckResult(
    bool IsNewer,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseNotes,
    string ReleaseUrl,
    string DownloadUrl,
    long AssetSizeBytes);
