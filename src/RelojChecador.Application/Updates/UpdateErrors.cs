using RelojChecador.Application.Common;

namespace RelojChecador.Application.Updates;

public static class UpdateErrors
{
    public static Error CheckFailed(string detail) =>
        new("Update.CheckFailed", $"No se pudo consultar si hay una versión nueva: {detail}");

    public static Error NoInstallerAsset() =>
        new("Update.NoInstallerAsset",
            "La última versión publicada en GitHub no tiene un instalador (.exe) adjunto.");

    public static Error VersionNotParseable(string rawVersion) =>
        new("Update.VersionNotParseable",
            $"No se pudo interpretar el número de versión publicado ('{rawVersion}').");

    public static Error DownloadFailed(string detail) =>
        new("Update.DownloadFailed", $"No se pudo descargar el instalador: {detail}");
}
