using System.Diagnostics;
using System.Windows;

namespace RelojChecador.WPF.Views;

/// <summary>
/// Diálogo mínimo para pegar la service_role key de Supabase y enlazar esta PC con la nube
/// desde "Conectar con nube" (ver UpdateViewModel.TryLinkCloudAsync) — antes esto exigía
/// editar %LocalAppData%\RelojChecador\appsettings.Local.json a mano y reiniciar la app (ver
/// RelojChecador.Infrastructure.Cloud/README.md). Solo captura el dato; guardarlo y
/// aplicarlo en memoria vive en SupabaseLocalConfigStore, no aquí.
/// </summary>
public partial class ConnectCloudDialog : Window
{
    private const string SupabaseDashboardUrl =
        "https://supabase.com/dashboard/project/vkvlucpjgvqrlvevcimq/settings/api-keys";

    public string ServiceRoleKey => ServiceRoleKeyPasswordBox.Password.Trim();

    public ConnectCloudDialog()
    {
        InitializeComponent();
    }

    private void OnOpenDashboardClick(object sender, RoutedEventArgs e)
    {
        // UseShellExecute=true: sin esto, .NET intenta ejecutar la URL como si fuera un
        // archivo/proceso y falla — es el mismo patrón ya usado en UpdateViewModel para
        // lanzar el instalador descargado.
        try
        {
            Process.Start(new ProcessStartInfo(SupabaseDashboardUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ErrorTextBlock.Text = $"No se pudo abrir el navegador: {ex.Message}. Copia este enlace a mano: {SupabaseDashboardUrl}";
            ErrorTextBlock.Visibility = Visibility.Visible;
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ServiceRoleKey))
        {
            ErrorTextBlock.Text = "Pega la clave service_role antes de continuar.";
            ErrorTextBlock.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}
