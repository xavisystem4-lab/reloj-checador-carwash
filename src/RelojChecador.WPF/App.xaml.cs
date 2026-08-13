using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelojChecador.Application.Devices;
using RelojChecador.Infrastructure.Data;
using RelojChecador.Infrastructure.Devices.Simulator;
using RelojChecador.Infrastructure.Logging;
using RelojChecador.WPF.ViewModels;
using Serilog;

namespace RelojChecador.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
/// <remarks>
/// Se usa "System.Windows.Application" totalmente calificado a propósito: el proyecto
/// "RelojChecador.Application" (capa de casos de uso) comparte el espacio de nombres raíz
/// "RelojChecador" con este proyecto WPF, así que el nombre "Application" sin calificar
/// resuelve al namespace RelojChecador.Application en vez de al tipo de WPF. Un using-alias
/// no basta aquí porque los usings de un namespace de archivo (file-scoped) se evalúan
/// después que los miembros del namespace ancestro "RelojChecador" — de ahí la ambigüedad.
/// </remarks>
public partial class App : System.Windows.Application
{
    private IHost? _host;
    private IServiceScope? _mainWindowScope;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelojChecador");
        Directory.CreateDirectory(appDataDirectory);
        var databasePath = Path.Combine(appDataDirectory, "relojchecador.db");
        var logDirectory = Path.Combine(appDataDirectory, "logs");

        _host = Host.CreateDefaultBuilder()
            .UseRelojChecadorLogging(logDirectory)
            .ConfigureServices((_, services) =>
            {
                services.AddRelojChecadorData($"Data Source={databasePath}");

                // Mientras no exista el adaptador real de ZKTeco (pendiente del SDK oficial —
                // ver memoria del proyecto), toda la app usa el simulador. Sustituir esta línea
                // por ZKTecoDeviceAdapter es el único cambio necesario cuando esté listo.
                services.AddSingleton<IAttendanceDeviceAdapter, SimulatorDeviceAdapter>();

                // Scoped, no Singleton: cada ventana principal recibe su propio DbContext con
                // vida acotada a esa ventana (ver el scope creado más abajo), en vez de
                // mantener un único DbContext abierto durante toda la sesión de la app.
                services.AddScoped<MainWindow>();
                services.AddScoped<MainViewModel>();
                services.AddScoped<DevicesViewModel>();
            })
            .Build();

        // Las migraciones corren en un scope efímero propio, independiente del de la ventana.
        using (var migrationScope = _host.Services.CreateScope())
        {
            var dbContext = migrationScope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
            dbContext.Database.Migrate();
        }

        Log.Information("RelojChecador iniciando. Base de datos local: {DatabasePath}", databasePath);

        _mainWindowScope = _host.Services.CreateScope();
        var mainWindow = _mainWindowScope.ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Closed += (_, _) => _mainWindowScope?.Dispose();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("RelojChecador cerrando.");
        Log.CloseAndFlush();
        _mainWindowScope?.Dispose();
        _host?.Dispose();
        base.OnExit(e);
    }
}
