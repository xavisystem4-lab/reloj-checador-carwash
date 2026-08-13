using Microsoft.Extensions.DependencyInjection;
using RelojChecador.Application.Updates;

namespace RelojChecador.Infrastructure.Updates;

public static class DependencyInjection
{
    public static IServiceCollection AddRelojChecadorUpdates(this IServiceCollection services)
    {
        services.AddHttpClient<IUpdateChecker, GitHubUpdateChecker>();
        return services;
    }
}
