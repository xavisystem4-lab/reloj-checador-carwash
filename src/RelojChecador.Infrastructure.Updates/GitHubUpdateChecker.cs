using System.Net.Http.Json;
using System.Reflection;
using RelojChecador.Application.Common;
using RelojChecador.Application.Updates;

namespace RelojChecador.Infrastructure.Updates;

/// <summary>
/// Consulta el último release publicado en GitHub (repo público
/// xavisystem4-lab/reloj-checador-carwash) usando la API REST pública, sin token — el
/// repo se hizo público justo para esto: evitar tener que guardar una credencial de
/// GitHub dentro de un .exe que se distribuye a varias sucursales (ver la decisión
/// documentada en el historial de commits del repo).
/// </summary>
public sealed class GitHubUpdateChecker(HttpClient httpClient) : IUpdateChecker
{
    // No hace falta que sean configurables por instalación: la app siempre se distribuye
    // desde este único repo/canal — a diferencia de la URL de Supabase (que sí podría
    // cambiar por negocio), aquí no hay nada que el usuario deba ajustar.
    private const string Owner = "xavisystem4-lab";
    private const string Repo = "reloj-checador-carwash";
    private const string InstallerAssetPrefix = "RelojChecador-Setup";

    public async Task<Result<UpdateCheckResult>> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");
            // La API de GitHub exige un User-Agent en cada solicitud (documentado) —
            // sin esto responde 403 sin importar que el repo sea público.
            request.Headers.UserAgent.ParseAdd("RelojChecador-App-UpdateChecker");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<UpdateCheckResult>(
                    UpdateErrors.CheckFailed($"GitHub respondió {(int)response.StatusCode} — {body}"));
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubReleaseDto>(cancellationToken);
            if (release is null)
            {
                return Result.Failure<UpdateCheckResult>(UpdateErrors.CheckFailed("respuesta vacía de GitHub"));
            }

            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.StartsWith(InstallerAssetPrefix, StringComparison.OrdinalIgnoreCase) &&
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
            if (asset is null)
            {
                return Result.Failure<UpdateCheckResult>(UpdateErrors.NoInstallerAsset());
            }

            var currentVersion = GetCurrentVersion();
            var latestVersionRaw = release.TagName;
            var latestVersionText = latestVersionRaw.TrimStart('v', 'V');

            if (!Version.TryParse(latestVersionText, out var latestVersion))
            {
                return Result.Failure<UpdateCheckResult>(UpdateErrors.VersionNotParseable(latestVersionRaw));
            }

            var isNewer = latestVersion > currentVersion;

            return Result.Success(new UpdateCheckResult(
                IsNewer: isNewer,
                CurrentVersion: FormatVersion(currentVersion),
                LatestVersion: FormatVersion(latestVersion),
                ReleaseNotes: release.Body,
                ReleaseUrl: release.HtmlUrl,
                DownloadUrl: asset.BrowserDownloadUrl,
                AssetSizeBytes: asset.Size));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result.Failure<UpdateCheckResult>(UpdateErrors.CheckFailed(ex.Message));
        }
    }

    public async Task<Result<string>> DownloadInstallerAsync(
        UpdateCheckResult update, IProgress<double>? progress, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileName = $"RelojChecador-Setup-{update.LatestVersion}.exe";
            var destinationPath = Path.Combine(Path.GetTempPath(), fileName);

            using var response = await httpClient.GetAsync(
                update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<string>(
                    UpdateErrors.DownloadFailed($"GitHub respondió {(int)response.StatusCode}"));
            }

            var totalBytes = response.Content.Headers.ContentLength;
            await using var httpStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, useAsync: true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await httpStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalRead += bytesRead;
                if (totalBytes is > 0)
                {
                    progress?.Report((double)totalRead / totalBytes.Value);
                }
            }

            return Result.Success(destinationPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Result.Failure<string>(UpdateErrors.DownloadFailed(ex.Message));
        }
    }

    /// <summary>La versión real viene de Directory.Build.props (&lt;Version&gt;, hoy
    /// "1.1.0") — MSBuild la escribe en el ensamblado de RelojChecador.WPF al compilar.
    /// Si por alguna razón no hay ensamblado de entrada (p. ej. corriendo bajo pruebas)
    /// cae a 0.0.0 en vez de reventar, para que CheckForUpdateAsync pueda seguir
    /// funcionando en cualquier contexto de ejecución.</summary>
    private static Version GetCurrentVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{Math.Max(version.Minor, 0)}.{Math.Max(version.Build, 0)}";
}
