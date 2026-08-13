using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelojChecador.Application.Updates;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel del botón "Actualizar versión" — visible en toda la app (ver
/// MainWindow.xaml, barra inferior), no solo en una pestaña, porque buscar/instalar
/// actualizaciones no es una operación de negocio de ninguna pantalla en particular.
///
/// Nunca actualiza en silencio: siempre confirma con un cuadro de diálogo antes de
/// descargar, y de nuevo antes de cerrar la app para instalar — igual que el resto del
/// proyecto nunca ejecuta acciones irreversibles sin que la persona lo pida
/// explícitamente.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateChecker _updateChecker;

    [ObservableProperty]
    private string _currentVersionLabel;

    [ObservableProperty]
    private string _updateStatusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    public UpdateViewModel(IUpdateChecker updateChecker)
    {
        _updateChecker = updateChecker;

        // Misma fuente que usa GitHubUpdateChecker para comparar (Directory.Build.props,
        // <Version>) — se lee aquí de forma independiente porque la versión debe verse en
        // pantalla ANTES de que el usuario presione "Actualizar versión", no solo después
        // de una consulta a GitHub.
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersionLabel = version is null ? "v?.?.?" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }

    [RelayCommand]
    private async Task CheckForUpdateAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        UpdateStatusMessage = "Buscando actualizaciones…";

        try
        {
            var checkResult = await _updateChecker.CheckForUpdateAsync();
            if (checkResult.IsFailure)
            {
                UpdateStatusMessage = $"No se pudo buscar actualizaciones: {checkResult.Error.Message}";
                Log.Warning("Falló la búsqueda de actualizaciones: {Error}", checkResult.Error.Message);
                return;
            }

            var update = checkResult.Value;
            if (!update.IsNewer)
            {
                UpdateStatusMessage = $"Ya tienes la versión más reciente (v{update.CurrentVersion}).";
                return;
            }

            var sizeText = update.AssetSizeBytes > 0
                ? $"{update.AssetSizeBytes / 1024.0 / 1024.0:0.0} MB"
                : "tamaño desconocido";
            var confirm = MessageBox.Show(
                $"Hay una versión nueva disponible: v{update.LatestVersion} (tienes v{update.CurrentVersion}).\n\n" +
                $"Tamaño de la descarga: {sizeText}.\n\n" +
                "¿Descargarla e instalarla ahora? La aplicación se cerrará para completar la instalación " +
                "(puede pedirte confirmación de Windows para instalar como administrador).",
                "Actualización disponible",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes)
            {
                UpdateStatusMessage = $"Hay una versión nueva (v{update.LatestVersion}) disponible cuando quieras instalarla.";
                return;
            }

            UpdateStatusMessage = "Descargando instalador…";
            var progress = new Progress<double>(p => UpdateStatusMessage = $"Descargando instalador… {p:P0}");
            var downloadResult = await _updateChecker.DownloadInstallerAsync(update, progress);
            if (downloadResult.IsFailure)
            {
                UpdateStatusMessage = $"No se pudo descargar la actualización: {downloadResult.Error.Message}";
                Log.Warning("Falló la descarga de la actualización: {Error}", downloadResult.Error.Message);
                return;
            }

            UpdateStatusMessage = "Iniciando el instalador…";
            Log.Information(
                "Lanzando instalador descargado desde {Path} para actualizar a v{Version}",
                downloadResult.Value, update.LatestVersion);

            // UseShellExecute=true es necesario para que Windows respete el
            // "requestedExecutionLevel=admin" del instalador (Inno Setup,
            // PrivilegesRequired=admin) y muestre el diálogo de UAC — Process.Start sin
            // ShellExecute no dispara esa elevación.
            Process.Start(new ProcessStartInfo(downloadResult.Value) { UseShellExecute = true });

            // La app debe cerrarse para que el instalador pueda sobrescribir el .exe en
            // uso — Inno Setup no puede reemplazar un archivo que este mismo proceso
            // todavía tiene abierto.
            System.Windows.Application.Current.Shutdown();
        }
        finally
        {
            IsBusy = false;
        }
    }
}
