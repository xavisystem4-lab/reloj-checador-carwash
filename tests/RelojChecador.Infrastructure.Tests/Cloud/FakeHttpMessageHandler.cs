using System.Net;

namespace RelojChecador.Infrastructure.Tests.Cloud;

/// <summary>Lo que capturó el fake de una solicitud HTTP saliente — se lee el cuerpo AQUÍ
/// (durante el envío), nunca guardando el HttpRequestMessage original: SupabaseRestClient
/// hace "using var request = ..." y lo dispone al terminar, así que su Content ya no sería
/// legible si se intentara leer después desde el test.</summary>
public sealed record CapturedRequest(HttpMethod Method, Uri? Uri, string? Body);

/// <summary>Doble de prueba para HttpMessageHandler — nunca toca la red real. Captura cada
/// solicitud enviada (método, URL, cuerpo ya leído) y responde con lo que diga
/// <see cref="ResponseFactory"/>, por defecto 200 OK vacío.</summary>
public sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<CapturedRequest> Requests { get; } = [];

    public Func<HttpRequestMessage, HttpResponseMessage> ResponseFactory { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.OK);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new CapturedRequest(request.Method, request.RequestUri, body));
        return ResponseFactory(request);
    }
}
