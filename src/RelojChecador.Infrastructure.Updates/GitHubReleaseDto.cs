using System.Text.Json.Serialization;

namespace RelojChecador.Infrastructure.Updates;

/// <summary>Subconjunto de la respuesta real de la API pública de GitHub
/// (GET /repos/{owner}/{repo}/releases/latest) — solo los campos que este adaptador
/// necesita, no un mapeo completo del schema de GitHub.</summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto> Assets { get; set; } = [];
}

public sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
