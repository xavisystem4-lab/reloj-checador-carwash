using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RelojChecador.Application.Updates;
using RelojChecador.Infrastructure.Cloud;
using RelojChecador.WPF.Views;
using Serilog;

namespace RelojChecador.WPF.ViewModels;

/// <summary>
/// ViewModel del botón "Actualizar versión" — visible en toda la app (ver
/// MainWindow.xaml, barra inferior), no solo en una pestaña, porque buscar/instalar
/// actualizaciones no es una operación de negocio de ninguna pantalla en particular.
/// También expone el estado de la sincronización con Supabase (ver
/// <see cref="CloudSyncStatusMessage"/>) — mismo criterio: es estado global de la app, no
/// de una pantalla en particular, y visible en la misma barra.
///
/// Nunca actualiza en silencio: siempre confirma con un cuadro de diálogo antes de
/// descargar, y de nuevo antes de cerrar la app para instalar — igual que el resto del
/// proyecto nunca ejecuta acciones irreversibles sin que la persona lo pida
/// explícitamente.
/// </summary>
public sealed partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateChecker _updateChecker;
    private readonly SupabaseSyncStatus _syncStatus;
    private readonly SupabaseSyncBackgroundService _syncService;
    private readonly SupabaseSyncOptions _syncOptions;
    private readonly SupabaseLocalConfigStore _localConfigStore;

    [ObservableProperty]
    private string _currentVersionLabel;

    [ObservableProperty]
    private string _updateStatusMessage = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _cloudSyncStatusMessage = "☁️ Nube: verificando…";

    /// <summary>Texto corto para el indicador de la barra superior — el mensaje completo
    /// (incluyendo el error exacto si lo hay) sigue disponible en
    /// <see cref="CloudSyncStatusMessage"/> vía el ToolTip del indicador.</summary>
    [ObservableProperty]
    private string _cloudSyncShortStatus = "Verificando…";

    /// <summary>"Disabled" | "Syncing" | "Connected" | "Error" — MainWindow.xaml usa
    /// DataTrigger sobre este valor para pintar el punto de estado (verde/rojo/gris) con
    /// los mismos tokens de color del tema actual, en vez de un color fijo que no
    /// respondería al cambio de modo claro/oscuro.</summary>
    [ObservableProperty]
    private string _cloudSyncStatusKind = "Unknown";

    [ObservableProperty]
    private bool _isCloudSyncBusy;

    public UpdateViewModel(
        IUpdateChecker updateChecker, SupabaseSyncStatus syncStatus, SupabaseSyncBackgroundService syncService,
        SupabaseSyncOptions syncOptions, SupabaseLocalConfigStore localConfigStore)
    {
        _updateChecker = updateChecker;
        _syncStatus = syncStatus;
        _syncService = syncService;
        _syncOptions = syncOptions;
        _localConfigStore = localConfigStore;

        // Misma fuente que usa GitHubUpdateChecker para comparar (Directory.Build.props,
        // <Version>) — se lee aquí de forma independiente porque la versión debe verse en
        // pantalla ANTES de que el usuario presione "Actualizar versión", no solo después
        // de una consulta a GitHub.
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersionLabel = version is null ? "v?.?.?" : $"v{version.Major}.{version.Minor}.{version.Build}";

        // SupabaseSyncStatus.Changed se dispara desde el hilo del BackgroundService, no el
        // de UI — hay que pasar por el Dispatcher antes de tocar una propiedad enlazada.
        _syncStatus.Changed += (_, _) =>
            System.Windows.Application.Current?.Dispatcher.Invoke(RefreshCloudSyncStatusMessage);
        RefreshCloudSyncStatusMessage();
    }

    /// <summary>Traduce el estado crudo de <see cref="SupabaseSyncStatus"/> a un mensaje
    /// legible — existe porque diagnosticar "no sube nada a Supabase" sin ver esto en
    /// pantalla resultó, en la práctica, muy lento (varias rondas de capturas de pantalla
    /// pedidas al usuario para revisar el archivo de logs a mano).</summary>
    private void RefreshCloudSyncStatusMessage()
    {
        if (!_syncStatus.IsConfigured)
        {
            CloudSyncStatusMessage = "☁️ Nube: sin configurar (la app funciona 100% local)";
            CloudSyncShortStatus = "Sin configurar";
            CloudSyncStatusKind = "Disabled";
            return;
        }

        if (_syncStatus.LastError is not null)
        {
            CloudSyncStatusMessage = $"☁️ Nube: error — {_syncStatus.LastError}";
            CloudSyncShortStatus = "Error";
            CloudSyncStatusKind = "Error";
            return;
        }

        if (_syncStatus.LastSuccessAtUtc is { } lastSuccessUtc)
        {
            var secondsAgo = Math.Max(0, (int)(DateTime.UtcNow - lastSuccessUtc).TotalSeconds);
            CloudSyncStatusMessage = $"☁️ Nube: conectado (hace {secondsAgo}s)";
            CloudSyncShortStatus = $"Conectado (hace {secondsAgo}s)";
            CloudSyncStatusKind = "Connected";
            return;
        }

        CloudSyncStatusMessage = "☁️ Nube: sincronizando…";
        CloudSyncShortStatus = "Sincronizando…";
        CloudSyncStatusKind = "Syncing";
    }

    /// <summary>Botón "Conectar con nube". Si esta instalación todavía no tiene la
    /// service_role key configurada, pedido explícito del usuario: en vez de solo fallar con
    /// "sin configurar" (como antes — el único camino era editar
    /// appsettings.Local.json a mano y reiniciar, ver README de Infrastructure.Cloud), este
    /// mismo botón abre el diálogo para pegarla y hacer el enlace ahí mismo
    /// (ver TryLinkCloudAsync). Ya con la nube enlazada (fuera antes o recién ahora), dispara
    /// un ciclo de sincronización de inmediato en vez de esperar hasta IntervalSeconds al
    /// siguiente ciclo automático, para poder confirmar en el momento si funcionó. El
    /// resultado se ve reflejado en <see cref="CloudSyncStatusMessage"/>
    /// (RefreshCloudSyncStatusMessage ya está suscrito a los cambios de estado).</summary>
    [RelayCommand]
    private async Task SyncCloudNowAsync()
    {
        if (IsCloudSyncBusy)
        {
            return;
        }

        if (!_syncOptions.IsConfigured && !await TryLinkCloudAsync())
        {
            return;
        }

        IsCloudSyncBusy = true;
        try
        {
            await _syncService.TriggerSyncNowAsync();
        }
        finally
        {
            IsCloudSyncBusy = false;
        }
    }

    /// <summary>Pide la service_role key (ver ConnectCloudDialog) y la guarda vía
    /// <see cref="SupabaseLocalConfigStore"/> — que además la aplica de inmediato sobre
    /// <see cref="_syncOptions"/> (la misma instancia Singleton que usa todo el motor de
    /// sincronización), así que la nube queda enlazada en ESTA sesión sin reiniciar la app.
    /// El ciclo automático de fondo (SupabaseSyncBackgroundService) también la recoge solo
    /// en su siguiente sondeo (cada 5s mientras no está configurado, ver ExecuteAsync).</summary>
    /// <returns>true si quedó enlazada (el llamador puede seguir con la sincronización);
    /// false si el usuario canceló o algo falló al guardar.</returns>
    private async Task<bool> TryLinkCloudAsync()
    {
        var dialog = new ConnectCloudDialog
        {
            Owner = System.Windows.Application.Current?.MainWindow,
        };
        if (dialog.ShowDialog() != true)
        {
            return false;
        }

        try
        {
            // No hace falta refrescar el mensaje aquí a mano: el llamador (SyncCloudNowAsync)
            // sigue de inmediato con TriggerSyncNowAsync(), que ya dispara sus propios
            // MarkAttemptStarted/MarkSuccess/MarkFailure sobre SupabaseSyncStatus — cada uno
            // dispara Changed, que ya está suscrito a RefreshCloudSyncStatusMessage.
            await _localConfigStore.SaveServiceRoleKeyAsync(dialog.ServiceRoleKey, _syncOptions);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "No se pudo guardar la clave de enlace con Supabase.");
            MessageBox.Show(
                System.Windows.Application.Current?.MainWindow,
                $"No se pudo guardar la clave: {ex.Message}",
                "No se pudo enlazar con la nube",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
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
