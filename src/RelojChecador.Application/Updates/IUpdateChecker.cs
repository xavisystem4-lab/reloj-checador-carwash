using RelojChecador.Application.Common;

namespace RelojChecador.Application.Updates;

/// <summary>Consulta y descarga actualizaciones de la app desde el canal de
/// distribución (hoy, GitHub Releases del repo público — ver
/// RelojChecador.Infrastructure.Updates). Nunca instala nada por sí solo: solo
/// consulta/descarga, la decisión de lanzar el instalador y cerrar la app es de la UI,
/// con confirmación explícita del usuario.</summary>
public interface IUpdateChecker
{
    Task<Result<UpdateCheckResult>> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    /// <summary>Descarga el instalador de <paramref name="update"/> a un archivo temporal
    /// y devuelve su ruta local. <paramref name="progress"/> reporta 0.0–1.0 cuando el
    /// servidor informa el tamaño total; si no lo informa, puede no reportar nada — quien
    /// llama no debe asumir que siempre habrá progreso incremental.</summary>
    Task<Result<string>> DownloadInstallerAsync(
        UpdateCheckResult update, IProgress<double>? progress, CancellationToken cancellationToken = default);
}
