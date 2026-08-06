using System.Net;
using System.Text;
using Jellyfin.Plugin.RequestBridge.Configuration;
using Jellyfin.Plugin.RequestBridge.Providers.Seerr;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// Configuration supplied directly, without a running plugin.
/// </summary>
internal sealed class StubConfigurationSource(string? baseUrl = "http://seerr.test", string? apiKey = "test-key")
    : IPluginConfigurationSource
{
    public PluginConfiguration? Current { get; } = baseUrl is null && apiKey is null
        ? null
        : new PluginConfiguration
        {
            ProviderBaseUrl = baseUrl ?? string.Empty,
            ProviderApiKey = apiKey ?? string.Empty,
        };
}

/// <summary>
/// A recorded outbound call.
/// </summary>
internal sealed record RecordedCall(HttpMethod Method, Uri? Uri, string? Body, string? ApiKey);

/// <summary>
/// An HTTP handler that answers from a script and records what it was asked.
/// </summary>
/// <remarks>
/// Used instead of a live server so the request body can be asserted on. The
/// bug that reached production was a serialised null in a request body, which no
/// amount of asserting on responses would have caught.
/// </remarks>
internal sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<RecordedCall> Calls { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        request.Headers.TryGetValues("X-Api-Key", out var keys);

        Calls.Add(new RecordedCall(request.Method, request.RequestUri, body, keys?.FirstOrDefault()));

        return respond(request);
    }
}

/// <summary>
/// A factory that hands out clients bound to a scripted handler.
/// </summary>
internal sealed class ScriptedClientFactory(ScriptedHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>
/// Builds a provider wired to a scripted server.
/// </summary>
internal static class SeerrTestHarness
{
    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static (SeerrRequestProvider Provider, ScriptedHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? baseUrl = "http://seerr.test",
        string? apiKey = "test-key")
    {
        var handler = new ScriptedHandler(respond);

        var provider = new SeerrRequestProvider(
            new ScriptedClientFactory(handler),
            new StubConfigurationSource(baseUrl, apiKey),
            new StubLogger<SeerrRequestProvider>());

        return (provider, handler);
    }

    /// <summary>
    /// A search response in the shape Seerr actually returns.
    /// </summary>
    public const string SearchResponse = """
        {
          "page": 1,
          "results": [
            {
              "id": 27205,
              "mediaType": "movie",
              "title": "Inception",
              "overview": "A thief who steals corporate secrets.",
              "posterPath": "/poster.jpg",
              "releaseDate": "2010-07-15"
            },
            {
              "id": 95396,
              "mediaType": "tv",
              "name": "Severance",
              "overview": "Mark leads a team of office workers.",
              "posterPath": "/severance.jpg",
              "firstAirDate": "2022-02-18",
              "mediaInfo": { "status": 5 }
            },
            {
              "id": 6193,
              "mediaType": "person",
              "name": "Leonardo DiCaprio"
            }
          ]
        }
        """;
}
