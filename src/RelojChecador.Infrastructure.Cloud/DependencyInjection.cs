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

        if (options.IsConfigured)
        {
            services.AddHttpClient<SupabaseRestClient>();
        }

        services.AddHostedService<SupabaseSyncBackgroundService>();
        return services;
    }
}
