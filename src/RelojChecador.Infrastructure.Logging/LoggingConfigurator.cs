using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace RelojChecador.Infrastructure.Logging;

/// <summary>
/// Configura Serilog como proveedor de logging estructurado del host. Escribe a un
/// archivo diario local además de la consola de depuración — nunca a Supabase
/// directamente: si algún día se necesita enviar telemetría a la nube, eso lo decide
/// el motor de sincronización leyendo estos archivos/eventos, no el logger en sí.
///
/// Responsabilidad de quien emite cada log (no de este configurador): jamás incluir
/// contraseñas, tokens de sesión ni datos biométricos en el mensaje.
/// </summary>
public static class LoggingConfigurator
{
    public static IHostBuilder UseRelojChecadorLogging(this IHostBuilder hostBuilder, string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);

        return hostBuilder.UseSerilog((_, _, configuration) =>
        {
            configuration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Debug()
                .WriteTo.File(
                    Path.Combine(logDirectory, "relojchecador-.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}{NewLine}{Message:lj}{NewLine}{Exception}");
        });
    }
}
