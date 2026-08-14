using System.Net;
using System.Text;
using RelojChecador.Infrastructure.Cloud;

namespace RelojChecador.Infrastructure.Tests.Cloud;

public class SupabaseRestClientTests
{
    private static SupabaseSyncOptions ConfiguredOptions() => new()
    {
        Url = "https://fake.supabase.co",
        ServiceRoleKey = "fake-service-role-key",
    };

    private sealed record SampleRow(Guid Id, string Name);

    [Fact]
    public async Task GetAsync_ArmaLaUrlConElQueryYDeserializaLaLista()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """[{"id":"11111111-1111-1111-1111-111111111111","name":"algo"}]""",
                    Encoding.UTF8, "application/json"),
            },
        };
        var client = new SupabaseRestClient(new HttpClient(handler), ConfiguredOptions());

        var result = await client.GetAsync<SampleRow>("sample_table", "status=eq.pending&limit=1", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("algo", result[0].Name);

        var sentRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, sentRequest.Method);
        Assert.Equal("https://fake.supabase.co/rest/v1/sample_table?status=eq.pending&limit=1", sentRequest.Uri!.ToString());
    }

    [Fact]
    public async Task GetAsync_SinFilas_DevuelveListaVacia()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            },
        };
        var client = new SupabaseRestClient(new HttpClient(handler), ConfiguredOptions());

        var result = await client.GetAsync<SampleRow>("sample_table", "status=eq.pending", CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAsync_RespuestaNoExitosa_LanzaConElCuerpoDelError()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
            {
                Content = new StringContent("clave invalida"),
            },
        };
        var client = new SupabaseRestClient(new HttpClient(handler), ConfiguredOptions());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync<SampleRow>("sample_table", "status=eq.pending", CancellationToken.None));

        Assert.Contains("clave invalida", ex.Message);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task PatchAsync_MandaElVerboFiltroYCuerpoCorrectos()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.NoContent),
        };
        var client = new SupabaseRestClient(new HttpClient(handler), ConfiguredOptions());

        await client.PatchAsync("sample_table", "id=eq.abc", new { status = "completed" }, CancellationToken.None);

        var sentRequest = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Patch, sentRequest.Method);
        Assert.Equal("https://fake.supabase.co/rest/v1/sample_table?id=eq.abc", sentRequest.Uri!.ToString());
        Assert.Contains("\"status\":\"completed\"", sentRequest.Body);
    }

    [Fact]
    public async Task PatchAsync_RespuestaNoExitosa_Lanza()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseFactory = _ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("dato invalido"),
            },
        };
        var client = new SupabaseRestClient(new HttpClient(handler), ConfiguredOptions());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.PatchAsync("sample_table", "id=eq.abc", new { status = "failed" }, CancellationToken.None));

        Assert.Contains("dato invalido", ex.Message);
    }
}
