using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RelojChecador.Application.Branches;
using RelojChecador.Application.Common;
using RelojChecador.Application.Devices;
using RelojChecador.Application.Employees;
using RelojChecador.Application.Identity;
using RelojChecador.Infrastructure.Data.Repositories;

namespace RelojChecador.Infrastructure.Data;

public static class DependencyInjection
{
    /// <summary>Registra el DbContext (SQLite) y los repositorios. <paramref name="sqliteConnectionString"/>
    /// normalmente apunta a un archivo bajo %LocalAppData%\RelojChecador — la resuelve quien compone
    /// la aplicación (RelojChecador.WPF), no esta capa.</summary>
    public static IServiceCollection AddRelojChecadorData(
        this IServiceCollection services, string sqliteConnectionString)
    {
        services.AddDbContext<RelojChecadorDbContext>(options =>
            options.UseSqlite(sqliteConnectionString));

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<RelojChecadorDbContext>());
        services.AddScoped<IBranchRepository, EfBranchRepository>();
        services.AddScoped<IEmployeeRepository, EfEmployeeRepository>();
        services.AddScoped<IDeviceRepository, EfDeviceRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();

        return services;
    }
}
