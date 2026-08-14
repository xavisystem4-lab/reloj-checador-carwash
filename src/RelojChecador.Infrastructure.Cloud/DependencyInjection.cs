using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RelojChecador.Infrastructure.Cloud;

public static class DependencyInjection
{
    /// <summary>Registra el motor de sincronización con Supabase. Seguro de llamar incluso
    /// sin <see cref="SupabaseSyncOptions.IsConfigured"/> — el servicio en segundo plano
    /// simplemente no hace nada en ese caso (ver SupabaseSyncBackgroundService.ExecuteAsync),
    /// para no romper el arranque de la app cuando todavía no hay credenciales de Supabase
    /// configuradas en esta instalación.</summary>
    public static IServiceCollection AddRelojChecadorCloudSync(
        this IServiceCollection services, SupabaseSyncOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<SupabaseSyncStatus>();

        if (options.IsConfigured)
        {
            services.AddHttpClient<SupabaseRestClient>();
        }

        // Registrado como Singleton (no solo como IHostedService) y AddHostedService
        // apunta a esa MISMA instancia — así el botón "Conectar con nube" de la UI puede
        // resolver SupabaseSyncBackgroundService directamente y llamar
        // TriggerSyncNowAsync() sobre el servicio real que ya está corriendo su ciclo
        // automático, en vez de crear una instancia aparte que nunca arranca.
        services.AddSingleton<SupabaseSyncBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<SupabaseSyncBackgroundService>());

        // RemoteSyncRequestCoordinator: siempre Singleton (igual que SupabaseSyncStatus) —
        // se autoprotege revisando IsConfigured en cada método, así que es seguro
        // inyectarlo en DevicesViewModel sin importar si esta instalación tiene Supabase
        // configurado o no. RemoteSyncRequestPollingService es quien realmente lo llama
        // cada IntervalSeconds — mismo patrón Singleton+AddHostedService de arriba.
        services.AddSingleton<RemoteSyncRequestCoordinator>();
        services.AddSingleton<RemoteSyncRequestPollingService>();
        services.AddHostedService(sp => sp.GetRequiredService<RemoteSyncRequestPollingService>());
        return services;
    }
}
