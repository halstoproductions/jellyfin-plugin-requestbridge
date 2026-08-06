using System.Net;
using RequestBridge.Abstractions;
using static Jellyfin.Plugin.RequestBridge.Tests.SeerrTestHarness;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// Integration tests for the Seerr provider against a scripted server.
/// </summary>
/// <remarks>
/// These exercise the parts that only fail when real bytes move: the request
/// body, the headers, the JSON shapes, and the translation of HTTP failures.
/// Every manual check against the live instance is reproduced here so it can be
/// run without one.
/// </remarks>
public class SeerrRequestProviderTests
{
    private static ExternalId Tmdb(string value = "27205") => new(ExternalIdSource.Tmdb, value);

    [Fact]
    public async Task Search_SendsTheApiKey()
    {
        var (provider, handler) = Create(_ => Json(SearchResponse));

        await provider.SearchAsync("inception", null, 10, CancellationToken.None);

        Assert.Equal("test-key", handler.Calls[0].ApiKey);
    }

    [Fact]
    public async Task Search_ParsesBothMediaShapes()
    {
        // Movies carry title and releaseDate; series carry name and firstAirDate.
        var (provider, _) = Create(_ => Json(SearchResponse));

        var results = await provider.SearchAsync("q", null, 10, CancellationToken.None);

        var movie = results.Single(item => item.MediaType == MediaType.Movie);
        Assert.Equal("Inception", movie.Title);
        Assert.Equal(2010, movie.Year);

        var series = results.Single(item => item.MediaType == MediaType.Series);
        Assert.Equal("Severance", series.Title);
        Assert.Equal(2022, series.Year);
    }

    [Fact]
    public async Task Search_DropsNonMediaResults()
    {
        // Seerr returns people alongside media. A person is not requestable and
        // must never reach a caller.
        var (provider, _) = Create(_ => Json(SearchResponse));

        var results = await provider.SearchAsync("q", null, 10, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.DoesNotContain(results, item => item.Title.Contains("DiCaprio", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_AppliesMediaTypeFilter()
    {
        var (provider, _) = Create(_ => Json(SearchResponse));

        var results = await provider.SearchAsync("q", MediaType.Series, 10, CancellationToken.None);

        Assert.Single(results);
        Assert.Equal(MediaType.Series, results[0].MediaType);
    }

    [Fact]
    public async Task Search_AppliesLimit()
    {
        var (provider, _) = Create(_ => Json(SearchResponse));

        Assert.Single(await provider.SearchAsync("q", null, 1, CancellationToken.None));
    }

    [Fact]
    public async Task Search_BuildsAbsolutePosterUrls()
    {
        var (provider, _) = Create(_ => Json(SearchResponse));

        var results = await provider.SearchAsync("q", MediaType.Movie, 10, CancellationToken.None);

        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", results[0].ImageUrl?.ToString());
    }

    [Fact]
    public async Task Request_OmitsSeasonsForAMovie()
    {
        // The regression that reached the live instance. Seerr rejects an
        // explicit null where it expects the key absent or an array, so this
        // asserts on the body rather than on the response.
        var (provider, handler) = Create(request =>
            request.Method == HttpMethod.Post
                ? Json("{}", HttpStatusCode.Created)
                : Json("""{ "id": 27205, "title": "Inception", "mediaInfo": { "status": 3 } }"""));

        await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);

        var post = handler.Calls.Single(call => call.Method == HttpMethod.Post);
        Assert.DoesNotContain("seasons", post.Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"mediaId\":27205", post.Body);
        Assert.Contains("\"mediaType\":\"movie\"", post.Body);
    }

    [Fact]
    public async Task Request_IncludesSeasonsForASeries()
    {
        var (provider, handler) = Create(request =>
            request.Method == HttpMethod.Post
                ? Json("{}", HttpStatusCode.Created)
                : Json("""{ "id": 95396, "name": "Severance", "mediaInfo": { "status": 3 } }"""));

        await provider.RequestAsync(new ExternalId(ExternalIdSource.Tmdb, "95396"), MediaType.Series, [1, 2], CancellationToken.None);

        var post = handler.Calls.Single(call => call.Method == HttpMethod.Post);
        Assert.Contains("\"seasons\":[1,2]", post.Body);
        Assert.Contains("\"mediaType\":\"tv\"", post.Body);
    }

    [Fact]
    public async Task Request_RereadsStateAfterwards()
    {
        // The creation response describes the request record, not the resulting
        // availability of the media, and the caller asked about the media.
        var (provider, handler) = Create(request =>
            request.Method == HttpMethod.Post
                ? Json("{}", HttpStatusCode.Created)
                : Json("""{ "id": 27205, "title": "Inception", "mediaInfo": { "status": 3 } }"""));

        var result = await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(RequestState.Processing, result.Item.State);
        Assert.Contains(handler.Calls, call => call.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Request_TreatsConflictAsAccepted()
    {
        // Already requested satisfies the caller's intent. A duplicate press must
        // not surface as an error.
        var (provider, _) = Create(request =>
            request.Method == HttpMethod.Post
                ? Json("""{ "message": "Request already exists" }""", HttpStatusCode.Conflict)
                : Json("""{ "id": 27205, "title": "Inception", "mediaInfo": { "status": 2 } }"""));

        var result = await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal(RequestState.Requested, result.Item.State);
    }

    [Fact]
    public async Task Request_RejectsSeasonsForAMovieBeforeCalling()
    {
        var (provider, handler) = Create(_ => Json("{}"));

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.RequestAsync(Tmdb(), MediaType.Movie, [1], CancellationToken.None));

        Assert.Equal(ProviderErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Request_RejectsNonTmdbIdentifiers()
    {
        var (provider, handler) = Create(_ => Json("{}"));

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.RequestAsync(new ExternalId(ExternalIdSource.Imdb, "tt1375666"), MediaType.Movie, null, CancellationToken.None));

        Assert.Equal(ProviderErrorCode.InvalidRequest, error.ErrorCode);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task GetItem_ReturnsNullForNonTmdbIdentifiers()
    {
        // Absence, not failure: another catalogue's id simply cannot be resolved.
        var (provider, handler) = Create(_ => Json("{}"));

        var item = await provider.GetItemAsync(
            new ExternalId(ExternalIdSource.Tvdb, "1234"), MediaType.Movie, CancellationToken.None);

        Assert.Null(item);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task GetItem_ReturnsNullOnNotFound()
    {
        var (provider, _) = Create(_ => Json("{}", HttpStatusCode.NotFound));

        Assert.Null(await provider.GetItemAsync(Tmdb(), MediaType.Movie, CancellationToken.None));
    }

    [Fact]
    public async Task GetItem_UsesTheSeriesEndpointForSeries()
    {
        var (provider, handler) = Create(_ => Json("""{ "id": 95396, "name": "Severance" }"""));

        await provider.GetItemAsync(new ExternalId(ExternalIdSource.Tmdb, "95396"), MediaType.Series, CancellationToken.None);

        Assert.Contains("/tv/95396", handler.Calls[0].Uri?.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorCode.ProviderRejected)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorCode.ProviderRejected)]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderErrorCode.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderErrorCode.ProviderRejected)]
    public async Task HttpFailures_BecomeClassifiedProviderErrors(HttpStatusCode status, ProviderErrorCode expected)
    {
        var (provider, _) = Create(_ => Json("""{ "message": "Seerr internal detail" }""", status));

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync("q", null, 10, CancellationToken.None));

        Assert.Equal(expected, error.ErrorCode);

        // Provider wording must never reach a caller.
        Assert.DoesNotContain("Seerr internal detail", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TransportFailure_BecomesUnreachable()
    {
        var (provider, _) = Create(_ => throw new HttpRequestException("connection refused"));

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync("q", null, 10, CancellationToken.None));

        Assert.Equal(ProviderErrorCode.ProviderUnreachable, error.ErrorCode);
    }

    [Fact]
    public async Task MalformedJson_BecomesRejected()
    {
        var (provider, _) = Create(_ => Json("not json at all"));

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync("q", null, 10, CancellationToken.None));

        Assert.Equal(ProviderErrorCode.ProviderRejected, error.ErrorCode);
    }

    [Fact]
    public async Task Unconfigured_ReportsNotConfigured()
    {
        var (provider, handler) = Create(_ => Json("{}"), baseUrl: string.Empty, apiKey: string.Empty);

        Assert.Equal(ProviderHealth.NotConfigured, provider.Capabilities.Health);

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync("q", null, 10, CancellationToken.None));

        Assert.Equal(ProviderErrorCode.ProviderNotConfigured, error.ErrorCode);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public async Task Health_TracksWhatTheLastCallSaw()
    {
        // Capabilities performs no network call, so health is the last observed
        // outcome. This is what lets discovery succeed while the provider is down.
        var fail = true;
        var (provider, _) = Create(_ => fail
            ? throw new HttpRequestException("down")
            : Json(SearchResponse));

        await Assert.ThrowsAsync<ProviderException>(() =>
            provider.SearchAsync("q", null, 10, CancellationToken.None));
        Assert.Equal(ProviderHealth.Unreachable, provider.Capabilities.Health);

        fail = false;
        await provider.SearchAsync("q", null, 10, CancellationToken.None);
        Assert.Equal(ProviderHealth.Healthy, provider.Capabilities.Health);
    }

    [Fact]
    public void Capabilities_AdvertiseSeasonSelection()
    {
        var (provider, _) = Create(_ => Json("{}"));

        Assert.True(provider.Capabilities.SupportsSeasonSelection);
        Assert.Contains(RequestState.PartiallyAvailable, provider.Capabilities.SupportedStates);
    }
}
