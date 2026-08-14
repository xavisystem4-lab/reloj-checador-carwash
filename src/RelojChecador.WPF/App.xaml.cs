using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RelojChecador.Application.Devices;
using RelojChecador.Infrastructure.Cloud;
using RelojChecador.Infrastructure.Data;
using RelojChecador.Infrastructure.Devices.ZKTeco;
using RelojChecador.Infrastructure.Logging;
using RelojChecador.Infrastructure.Updates;
using RelojChecador.WPF.Services;
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
    private readonly string _appDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RelojChecador");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Manejadores globales instalados ANTES que nada más pueda fallar: sin esto, una
        // excepción no capturada en cualquier punto (arranque, un "async void" de un
        // Loaded, un hilo de background) tumba el proceso completo sin dejar rastro visible
        // — que fue exactamente el síntoma reportado ("abre y se cierra") al probar el
        // instalador por primera vez en Windows real. Con esto, en vez de un cierre mudo,
        // el usuario ve un cuadro de diálogo con el motivo y queda un log del crash.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            ReportFatal(args.ExceptionObject as Exception, "AppDomain.UnhandledException", args.IsTerminating);
        Dispatcher.UnhandledException += (_, args) =>
        {
            ReportFatal(args.Exception, "Dispatcher.UnhandledException", isTerminating: false);
            args.Handled = true; // evita que WPF vuelva a tumbar el proceso tras mostrar el aviso
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ReportFatal(args.Exception, "TaskScheduler.UnobservedTaskException", isTerminating: false);
            args.SetObserved();
        };

        try
        {
            Directory.CreateDirectory(_appDataDirectory);
            var databasePath = Path.Combine(_appDataDirectory, "relojchecador.db");
            var logDirectory = Path.Combine(_appDataDirectory, "logs");
            var localSettingsPath = Path.Combine(_appDataDirectory, "appsettings.Local.json");

            _host = Host.CreateDefaultBuilder()
                .UseRelojChecadorLogging(logDirectory)
                .ConfigureAppConfiguration(config =>
                {
                    // appsettings.json (junto al .exe, commiteado): Url + AnonKey de Supabase,
                    // seguros de ser públicos. appsettings.Local.json (en %LocalAppData%, NUNCA
                    // en el repo ni en la carpeta de instalación): ServiceRoleKey real de esta
                    // instalación — cada sucursal la configura una sola vez a mano. Ambos son
                    // "optional: true": sin ninguno de los dos, la app arranca igual, 100% local
                    // (ver SupabaseSyncOptions.IsConfigured).
                    config.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true, reloadOnChange: false);
                    config.AddJsonFile(localSettingsPath, optional: true, reloadOnChange: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddRelojChecadorData($"Data Source={databasePath}");

                    // ZKTecoDeviceAdapter (COM de 32 bits, ver third-party/zkteco-sdk/README.md)
                    // reemplaza a SimulatorDeviceAdapter ahora que hay un reloj F22/ID real en
                    // campo. Si algún día hace falta volver al simulador (desarrollo sin
                    // hardware a la mano), esta es la única línea que hay que cambiar.
                    services.AddSingleton<IAttendanceDeviceAdapter, ZKTecoDeviceAdapter>();

                    var supabaseOptions = context.Configuration.GetSection("Supabase").Get<SupabaseSyncOptions>()
                        ?? new SupabaseSyncOptions();
                    services.AddRelojChecadorCloudSync(supabaseOptions);
                    services.AddRelojChecadorUpdates();

                    // Singleton, no Scoped: el tema es un estado global de la app (una sola
                    // preferencia, compartida por todas las ventanas), no algo ligado a la
                    // vida de una ventana en particular.
                    services.AddSingleton(new ThemeService(_appDataDirectory));

                    // Scoped, no Singleton: cada ventana principal recibe su propio DbContext con
                    // vida acotada a esa ventana (ver el scope creado más abajo), en vez de
                    // mantener un único DbContext abierto durante toda la sesión de la app.
                    services.AddScoped<MainWindow>();
                    services.AddScoped<MainViewModel>();
                    services.AddScoped<EmployeesViewModel>();
                    services.AddScoped<DevicesViewModel>();
                    services.AddScoped<AttendanceViewModel>();
                    services.AddScoped<PayrollViewModel>();
                    services.AddScoped<UpdateViewModel>();
                })
                .Build();

            // Las migraciones corren en un scope efímero propio, independiente del de la ventana.
            using (var migrationScope = _host.Services.CreateScope())
            {
                var dbContext = migrationScope.ServiceProvider.GetRequiredService<RelojChecadorDbContext>();
                dbContext.Database.Migrate();
            }

            // CRÍTICO — sin esto, ningún IHostedService (hoy: SupabaseSyncBackgroundService,
            // el único registrado en toda la solución) arranca su ExecuteAsync. Bug real
            // encontrado al diseñar la sincronización remota: Host.CreateDefaultBuilder().Build()
            // solo CONSTRUYE el host, nunca lo arranca — eso requiere Start()/StartAsync()
            // explícito, que nunca se llamaba aquí. En la práctica esto significaba que el
            // ciclo automático cada 10s (IntervalSeconds) NUNCA corrió por sí solo: todo lo
            // que parecía sincronizar "solo" ocurría por llamadas directas a
            // TriggerSyncNowAsync() (el botón "Conectar con nube", y los triggers atados a
            // marcaciones agregados en v1.10.13) — cualquier cambio que no pasara por esos
            // triggers puntuales (editar un empleado, una sucursal, un vínculo) dependía 100%
            // de que alguien presionara "Conectar con nube" a mano.
            _host.Start();

            Log.Information("RelojChecador iniciando. Base de datos local: {DatabasePath}", databasePath);

            // Se aplica el tema guardado ANTES de crear la ventana principal — así abre
            // directamente con el tema correcto, sin un parpadeo inicial en claro.
            _host.Services.GetRequiredService<ThemeService>().Initialize();

            _mainWindowScope = _host.Services.CreateScope();
            var mainWindow = _mainWindowScope.ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Closed += (_, _) => _mainWindowScope?.Dispose();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // Cualquier falla durante el arranque (migración, construcción del host, creación
            // de la ventana) se reporta igual que un crash tardío, en vez de dejar que la
            // excepción suba sin capturar y cierre la app en silencio.
            ReportFatal(ex, "OnStartup", isTerminating: true);
            Shutdown(-1);
        }
    }

    /// <summary>
    /// Último recurso ante un error que de otro modo cerraría la app sin explicación:
    /// intenta dejar constancia en el log de Serilog (puede no estar listo si el crash
    /// ocurrió antes de configurarlo, de ahí el respaldo en texto plano) y, sobre todo,
    /// SIEMPRE muestra un cuadro de diálogo visible — para no repetir un cierre mudo.
    /// </summary>
    private void ReportFatal(Exception? ex, string source, bool isTerminating)
    {
        var text = ex?.ToString() ?? "(excepción nula)";
        try
        {
            Log.Fatal(ex, "Error fatal no controlado en {Source} (isTerminating={IsTerminating})", source, isTerminating);
            Log.CloseAndFlush();
        }
        catch
        {
            // Serilog podría no estar configurado todavía si el crash ocurrió muy temprano.
        }

        try
        {
            Directory.CreateDirectory(_appDataDirectory);
            var crashLogPath = Path.Combine(_appDataDirectory, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(crashLogPath,
                $"Fuente: {source}{Environment.NewLine}Terminando el proceso: {isTerminating}{Environment.NewLine}{Environment.NewLine}{text}");
        }
        catch
        {
            // Si ni siquiera se puede escribir el archivo de respaldo, al menos queda el MessageBox.
        }

        try
        {
            System.Windows.MessageBox.Show(
                $"RelojChecador encontró un error inesperado y no puede continuar.\n\n" +
                $"Origen: {source}\n\n{text}\n\n" +
                $"Se guardó el detalle en:\n{_appDataDirectory}",
                "RelojChecador — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Si tampoco se puede mostrar el MessageBox, ya no queda nada más por intentar.
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("RelojChecador cerrando.");
        Log.CloseAndFlush();
        _mainWindowScope?.Dispose();

        try
        {
            // Contraparte de _host.Start() en OnStartup — para ordenadamente los
            // IHostedService (les da hasta 5s para terminar su ciclo actual) antes de
            // liberar el host. Envuelto en try/catch: cerrar la app nunca debe fallar por
            // esto, en el peor caso Dispose() de abajo corta lo que quede.
            _host?.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "No se pudo detener el host de forma ordenada al cerrar.");
        }

        _host?.Dispose();
        base.OnExit(e);
    }
}
