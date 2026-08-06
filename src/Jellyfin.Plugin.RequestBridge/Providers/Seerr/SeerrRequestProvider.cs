using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Providers.Seerr;

/// <summary>
/// A request provider backed by Seerr.
/// </summary>
/// <remarks>
/// <para>
/// The only component in the system permitted to know Seerr exists. Nothing above
/// <see cref="IRequestProvider"/> may name it, and no Seerr concept, status value,
/// or error string may escape this class.
/// </para>
/// <para>
/// Configuration is read on every call rather than captured at construction.
/// Service registration runs before the plugin instance exists, and an
/// administrator may change the URL or key at any time.
/// </para>
/// </remarks>
public sealed class SeerrRequestProvider : IRequestProvider
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string ApiRoot = "api/v1";
    private const string PosterBaseUrl = "https://image.tmdb.org/t/p/w500";
    private const string MediaTypeMovie = "movie";
    private const string MediaTypeTv = "tv";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SeerrRequestProvider> _logger;

    // Last observed reachability. Capabilities must not perform network calls, so
    // health is what the most recent real call saw rather than a fresh probe.
    private ProviderHealth _observedHealth = ProviderHealth.Healthy;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeerrRequestProvider"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory for the outbound client.</param>
    /// <param name="logger">Logger for this provider.</param>
    public SeerrRequestProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<SeerrRequestProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProviderCapabilities Capabilities => new("Seerr", CurrentHealth())
    {
        SupportedMediaTypes = [MediaType.Movie, MediaType.Series],

        // Every state this provider can genuinely reach. Seerr cannot distinguish
        // searching from downloading from importing, so those are absent by
        // design rather than by omission.
        SupportedStates =
        [
            RequestState.Unknown,
            RequestState.Requestable,
            RequestState.Requested,
            RequestState.Processing,
            RequestState.PartiallyAvailable,
            RequestState.Available,
        ],

        CanSearch = true,
        CanRequest = true,
        CanReportStatus = true,
        SupportsSeasonSelection = true,
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<RequestItem>> SearchAsync(
        string query,
        MediaType? mediaType,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var response = await GetAsync<SeerrSearchResponse>(
            $"search?query={Uri.EscapeDataString(query)}", cancellationToken).ConfigureAwait(false);

        var results = response?.Results ?? [];

        return [.. results
            // Search returns people alongside media. Anything that is not a
            // requestable media type is dropped here so it never reaches a caller.
            .Where(result => ToMediaType(result.MediaType) is not null)
            .Where(result => mediaType is null || ToMediaType(result.MediaType) == mediaType)
            .Take(limit)
            .Select(ToRequestItem)];
    }

    /// <inheritdoc />
    public async Task<RequestItem?> GetItemAsync(
        ExternalId id,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (id.Source != ExternalIdSource.Tmdb)
        {
            // Seerr addresses media by TMDB id. Another catalogue's id cannot be
            // resolved, which is an absence rather than a failure.
            return null;
        }

        var path = mediaType == MediaType.Series ? "tv" : "movie";

        var detail = await GetAsync<SeerrSearchResult>(
            $"{path}/{Uri.EscapeDataString(id.Value)}", cancellationToken).ConfigureAwait(false);

        if (detail is null)
        {
            return null;
        }

        // Detail responses omit mediaType, so it is supplied from the request.
        detail.MediaType = mediaType == MediaType.Series ? MediaTypeTv : MediaTypeMovie;

        return ToRequestItem(detail);
    }

    /// <inheritdoc />
    public async Task<RequestResult> RequestAsync(
        ExternalId id,
        MediaType mediaType,
        IReadOnlyList<int>? seasons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (mediaType == MediaType.Movie && seasons is { Count: > 0 })
        {
            throw new ProviderException(
                ProviderErrorCode.InvalidRequest, "Seasons cannot be requested for a movie.");
        }

        if (id.Source != ExternalIdSource.Tmdb || !int.TryParse(id.Value, CultureInfo.InvariantCulture, out var tmdbId))
        {
            throw new ProviderException(
                ProviderErrorCode.InvalidRequest, "This provider can only request items identified by TMDB id.");
        }

        var body = new SeerrCreateRequest
        {
            MediaType = mediaType == MediaType.Series ? MediaTypeTv : MediaTypeMovie,
            MediaId = tmdbId,
            Seasons = mediaType == MediaType.Series ? seasons : null,
        };

        using var client = CreateClient();
        using var content = JsonContent.Create(body, options: _json);

        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(new Uri($"{ApiRoot}/request", UriKind.Relative), content, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _observedHealth = ProviderHealth.Unreachable;
            throw new ProviderException(
                ProviderErrorCode.ProviderUnreachable, "The request provider could not be reached.", ex);
        }

        using (response)
        {
            // Already requested is not a failure. The caller's intent is satisfied
            // either way, and a duplicate press must not surface as an error.
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
            {
                await LogFailureAsync(response, "request creation", cancellationToken).ConfigureAwait(false);
                throw ToProviderException(response.StatusCode);
            }
        }

        _observedHealth = ProviderHealth.Healthy;

        // Re-read rather than trusting the creation response, which reports the
        // request record and not the resulting availability of the media.
        var item = await GetItemAsync(id, mediaType, cancellationToken).ConfigureAwait(false)
            ?? new RequestItem([id], mediaType, id.Value, RequestState.Requested);

        return new RequestResult(true, item);
    }

    private static MediaType? ToMediaType(string? seerrMediaType) => seerrMediaType switch
    {
        MediaTypeMovie => MediaType.Movie,
        MediaTypeTv => MediaType.Series,
        _ => null,
    };

    private static int? ToYear(string? date) =>
        DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Year
            : null;

    private static RequestItem ToRequestItem(SeerrSearchResult result)
    {
        var mediaType = ToMediaType(result.MediaType) ?? MediaType.Movie;

        var title = (mediaType == MediaType.Series ? result.Name : result.Title)
            ?? result.Title
            ?? result.Name
            ?? result.Id.ToString(CultureInfo.InvariantCulture);

        var id = new ExternalId(ExternalIdSource.Tmdb, result.Id.ToString(CultureInfo.InvariantCulture));

        return new RequestItem([id], mediaType, title, SeerrStateMapper.ToRequestState(result.MediaInfo))
        {
            Year = ToYear(mediaType == MediaType.Series ? result.FirstAirDate : result.ReleaseDate),
            Overview = string.IsNullOrWhiteSpace(result.Overview) ? null : result.Overview,
            ImageUrl = string.IsNullOrWhiteSpace(result.PosterPath)
                ? null
                : new Uri($"{PosterBaseUrl}{result.PosterPath}"),
        };
    }

    private static ProviderException ToProviderException(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new ProviderException(
            ProviderErrorCode.ProviderRejected, "The request provider rejected the credentials."),

        HttpStatusCode.NotFound => new ProviderException(
            ProviderErrorCode.ItemNotFound, "The request provider does not know this item."),

        HttpStatusCode.TooManyRequests => new ProviderException(
            ProviderErrorCode.RateLimited, "The request provider is being called too often."),

        _ => new ProviderException(
            ProviderErrorCode.ProviderRejected, "The request provider refused the operation."),
    };

    /// <summary>
    /// Records what the provider actually said when it refused.
    /// </summary>
    /// <remarks>
    /// The exception deliberately carries only a neutral code and message, so
    /// that provider wording never reaches a caller. That leaves the log as the
    /// only place an administrator can find out what went wrong, which makes
    /// discarding the status code and body here a diagnostics dead end.
    /// </remarks>
    private async Task LogFailureAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        string body;
        try
        {
            body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Do not catch general exception types
        catch (Exception ex)
#pragma warning restore CA1031
        {
            body = $"<unreadable: {ex.GetType().Name}>";
        }

        const int MaxLoggedBodyLength = 500;
        if (body.Length > MaxLoggedBodyLength)
        {
            body = body[..MaxLoggedBodyLength];
        }

        _logger.LogWarning(
            "Request provider refused {Operation} with {StatusCode}. Response: {Body}",
            operation,
            (int)response.StatusCode,
            body);
    }

    private ProviderHealth CurrentHealth() =>
        IsConfigured(out _, out _) ? _observedHealth : ProviderHealth.NotConfigured;

    private static bool IsConfigured(out string baseUrl, out string apiKey)
    {
        var configuration = Plugin.Instance?.Configuration;

        baseUrl = configuration?.ProviderBaseUrl?.Trim() ?? string.Empty;
        apiKey = configuration?.ProviderApiKey?.Trim() ?? string.Empty;

        return baseUrl.Length > 0 && apiKey.Length > 0;
    }

    private HttpClient CreateClient()
    {
        if (!IsConfigured(out var baseUrl, out var apiKey))
        {
            throw new ProviderException(
                ProviderErrorCode.ProviderNotConfigured, "No request provider is configured on this server.");
        }

        var client = _httpClientFactory.CreateClient(NamedClient.Default);
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + '/');
        client.DefaultRequestHeaders.Add(ApiKeyHeader, apiKey);

        return client;
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        using var client = CreateClient();

        try
        {
            using var response = await client
                .GetAsync(new Uri($"{ApiRoot}/{path}", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _observedHealth = ProviderHealth.Healthy;
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                _observedHealth = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? ProviderHealth.Degraded
                    : ProviderHealth.Healthy;

                await LogFailureAsync(response, path, cancellationToken).ConfigureAwait(false);
                throw ToProviderException(response.StatusCode);
            }

            _observedHealth = ProviderHealth.Healthy;

            return await response.Content
                .ReadFromJsonAsync<T>(_json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _observedHealth = ProviderHealth.Unreachable;
            _logger.LogWarning(ex, "Request provider unreachable at {Path}.", path);

            throw new ProviderException(
                ProviderErrorCode.ProviderUnreachable, "The request provider could not be reached.", ex);
        }
        catch (JsonException ex)
        {
            _observedHealth = ProviderHealth.Degraded;

            throw new ProviderException(
                ProviderErrorCode.ProviderRejected, "The request provider returned an unreadable response.", ex);
        }
    }
}
