using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RelojChecador.Infrastructure.Cloud;

namespace RelojChecador.Infrastructure.Tests.Cloud;

public class RemoteSyncRequestCoordinatorTests
{
    private static SupabaseSyncOptions ConfiguredOptions() => new()
    {
        Url = "https://fake.supabase.co",
        ServiceRoleKey = "fake-service-role-key",
    };

    private static RemoteSyncRequestCoordinator BuildCoordinator(FakeHttpMessageHandler handler, SupabaseSyncOptions options)
    {
        var restClient = new SupabaseRestClient(new HttpClient(handler), options);
        var scopeFactory = new FixedServiceScopeFactory(restClient);
        return new RemoteSyncRequestCoordinator(scopeFactory, options, NullLogger<RemoteSyncRequestCoordinator>.Instance);
    }

    private static HttpResponseMessage PendingRow(string id) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""[{"id":"{{id}}","status":"pending","requested_by_email":"dueno@carwash.mx","requested_at_utc":"2026-08-14T10:00:00Z","started_at_utc":null,"completed_at_utc":null,"result_summary":null,"error_message":null}]""",
            Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task PollForPendingRequestAsync_ConSolicitudPendiente_LaMarcaEnCursoYDisparaElEvento()
    {
        const string id = "11111111-1111-1111-1111-111111111111";
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = request => request.Method == HttpMethod.Get
                ? PendingRow(id)
                : new HttpResponseMessage(HttpStatusCode.NoContent), // PATCH a in_progress
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());

        RemoteSyncRequest? received = null;
        coordinator.SyncRequested += (_, request) => received = request;

        await coordinator.PollForPendingRequestAsync(CancellationToken.None);

        Assert.NotNull(received);
        Assert.Equal(Guid.Parse(id), received!.Id);
        Assert.Equal("dueno@carwash.mx", received.RequestedByEmail);

        // Se marcó in_progress ANTES de disparar el evento — no después.
        var patchRequest = Assert.Single(handler.Requests, r => r.Method == HttpMethod.Patch);
        Assert.Contains("\"status\":\"in_progress\"", patchRequest.Body);
    }

    [Fact]
    public async Task PollForPendingRequestAsync_SinSolicitudesPendientes_NoDisparaNada()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            },
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());

        var disparado = false;
        coordinator.SyncRequested += (_, _) => disparado = true;

        await coordinator.PollForPendingRequestAsync(CancellationToken.None);

        Assert.False(disparado);
    }

    [Fact]
    public async Task PollForPendingRequestAsync_SinSupabaseConfigurado_NiSiquieraIntentaResolverElClienteHttp()
    {
        // A propósito NO usa BuildCoordinator: SupabaseRestClient se niega a construirse
        // sin Url/ServiceRoleKey (ver su propio constructor), así que la única forma
        // válida de probar "sin configurar" es confirmar que PollForPendingRequestAsync
        // corta ANTES de siquiera pedirle al scope que resuelva uno — igual que hace
        // SupabaseSyncBackgroundService.ExecuteAsync.
        var scopeFactory = new ThrowingServiceScopeFactory();
        var coordinator = new RemoteSyncRequestCoordinator(
            scopeFactory, new SupabaseSyncOptions(), NullLogger<RemoteSyncRequestCoordinator>.Instance);

        await coordinator.PollForPendingRequestAsync(CancellationToken.None); // no debe lanzar

        Assert.False(scopeFactory.WasCalled);
    }

    [Fact]
    public async Task PollForPendingRequestAsync_ConUnaSolicitudYaActiva_NoVuelveAConsultar()
    {
        const string id = "22222222-2222-2222-2222-222222222222";
        var getCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = request =>
            {
                if (request.Method != HttpMethod.Get)
                {
                    return new HttpResponseMessage(HttpStatusCode.NoContent);
                }

                getCount++;
                return PendingRow(id);
            },
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());
        coordinator.SyncRequested += (_, _) => { }; // deliberadamente nunca completa la solicitud

        await coordinator.PollForPendingRequestAsync(CancellationToken.None); // primera vez: la recoge
        await coordinator.PollForPendingRequestAsync(CancellationToken.None); // segunda vez: sigue "activa" en memoria

        Assert.Equal(1, getCount); // el guardia en memoria evitó la segunda consulta
    }

    [Fact]
    public async Task CompleteAsync_ConExito_MandaCompletedConElResumen()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());
        var requestId = Guid.NewGuid();

        await coordinator.CompleteAsync(requestId, success: true, "3 marcaciones nuevas.", CancellationToken.None);

        var patchRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, patchRequest.Method);
        Assert.Contains($"id=eq.{requestId}", patchRequest.Uri!.ToString());
        Assert.Contains("\"status\":\"completed\"", patchRequest.Body);
        Assert.Contains("\"result_summary\":\"3 marcaciones nuevas.\"", patchRequest.Body);
    }

    [Fact]
    public async Task CompleteAsync_ConFallo_MandaFailedConElMensajeDeError()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());
        var requestId = Guid.NewGuid();

        await coordinator.CompleteAsync(requestId, success: false, "No se pudo conectar.", CancellationToken.None);

        var patchRequest = Assert.Single(handler.Requests);
        Assert.Contains("\"status\":\"failed\"", patchRequest.Body);
        Assert.Contains("\"error_message\":\"No se pudo conectar.\"", patchRequest.Body);
    }

    [Fact]
    public async Task CompleteAsync_LiberaElGuardiaParaQuePuedaRecogerOtraSolicitud()
    {
        const string id = "33333333-3333-3333-3333-333333333333";
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = request => request.Method == HttpMethod.Get
                ? PendingRow(id)
                : new HttpResponseMessage(HttpStatusCode.NoContent),
        };
        var coordinator = BuildCoordinator(handler, ConfiguredOptions());
        var disparos = 0;
        coordinator.SyncRequested += (_, _) => disparos++;

        await coordinator.PollForPendingRequestAsync(CancellationToken.None);
        await coordinator.CompleteAsync(Guid.Parse(id), success: true, "ok", CancellationToken.None);
        await coordinator.PollForPendingRequestAsync(CancellationToken.None); // ya liberado: puede volver a recogerla

        Assert.Equal(2, disparos);
    }

    /// <summary>Fake mínimo de IServiceScopeFactory que siempre resuelve la MISMA instancia
    /// de SupabaseRestClient — evita depender del contenedor DI concreto
    /// (Microsoft.Extensions.DependencyInjection) solo para esta prueba; basta con las
    /// abstracciones, ya disponibles vía Infrastructure.Cloud.</summary>
    private sealed class FixedServiceScopeFactory(SupabaseRestClient restClient)
        : IServiceScopeFactory, IServiceScope, IServiceProvider
    {
        public IServiceProvider ServiceProvider => this;
        public IServiceScope CreateScope() => this;
        public object? GetService(Type serviceType) => serviceType == typeof(SupabaseRestClient) ? restClient : null;
        public void Dispose()
        {
        }
    }

    /// <summary>Fake que registra si alguna vez se le pidió un scope — usado para probar
    /// que "sin configurar" corta antes de intentar resolver nada.</summary>
    private sealed class ThrowingServiceScopeFactory : IServiceScopeFactory
    {
        public bool WasCalled { get; private set; }

        public IServiceScope CreateScope()
        {
            WasCalled = true;
            throw new InvalidOperationException("No debería pedirse un scope cuando Supabase no está configurado.");
        }
    }
}
