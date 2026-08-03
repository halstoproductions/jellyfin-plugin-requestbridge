using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Providers;

/// <summary>
/// A provider that fulfils nothing, used to prove the architecture end to end.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately boring. It reports <see cref="RequestState.Requestable"/> before
/// a request and <see cref="RequestState.Requested"/> afterwards, and never
/// advances further. There is no persistence, no timer, and no simulated
/// progression.
/// </para>
/// <para>
/// A fake that appeared to move through <see cref="RequestState.Processing"/>
/// towards <see cref="RequestState.Available"/> would be a fake progress bar
/// wearing a different hat. It would also test the fake rather than the
/// architecture, since nothing downstream would have produced those states.
/// </para>
/// <para>
/// Registered as a singleton, so it is called concurrently and its state is held
/// in a concurrent collection.
/// </para>
/// </remarks>
public sealed class FakeRequestProvider : IRequestProvider
{
    private const int FakeSearchResultCount = 3;

    private readonly ConcurrentDictionary<(ExternalIdSource Source, string Value, MediaType Type), byte> _requested = new();
    private readonly ILogger<FakeRequestProvider> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FakeRequestProvider"/> class.
    /// </summary>
    /// <param name="logger">Logger for this provider.</param>
    public FakeRequestProvider(ILogger<FakeRequestProvider> logger)
    {
        _logger = logger;

        Capabilities = new ProviderCapabilities("Fake Provider", ProviderHealth.Healthy)
        {
            SupportedMediaTypes = [MediaType.Movie, MediaType.Series],

            // Only the states this provider can actually reach. Advertising
            // Processing or Available here would be a lie a client could not
            // detect.
            SupportedStates = [RequestState.Unknown, RequestState.Requestable, RequestState.Requested],

            CanSearch = true,
            CanRequest = true,
            CanReportStatus = true,

            // Seasons are accepted and ignored, so this is false rather than
            // true-but-meaningless. A client must not offer a season picker.
            SupportsSeasonSelection = false,
        };
    }

    /// <inheritdoc />
    public ProviderCapabilities Capabilities { get; }

    /// <inheritdoc />
    public Task<IReadOnlyList<RequestItem>> SearchAsync(
        string query,
        MediaType? mediaType,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        cancellationToken.ThrowIfCancellationRequested();

        var type = mediaType ?? MediaType.Movie;
        var count = Math.Min(FakeSearchResultCount, Math.Max(0, limit));

        var results = new List<RequestItem>(count);
        for (var i = 1; i <= count; i++)
        {
            // Derived from the query so that results are stable for a given
            // search, which makes a subsequent request or status call meaningful.
            var id = new ExternalId(ExternalIdSource.Tmdb, $"{Math.Abs(query.GetHashCode(StringComparison.Ordinal)) % 100000}{i}");

            results.Add(Describe(id, type, $"{query} ({i})"));
        }

        _logger.LogDebug("Fake provider returned {Count} results for {Query}.", results.Count, query);

        return Task.FromResult<IReadOnlyList<RequestItem>>(results);
    }

    /// <inheritdoc />
    public Task<RequestItem?> GetItemAsync(
        ExternalId id,
        MediaType mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        cancellationToken.ThrowIfCancellationRequested();

        // Every id is known to this provider, so absence is never reported. A
        // real provider returns null for an unknown item.
        return Task.FromResult<RequestItem?>(Describe(id, mediaType, $"Fake {mediaType} {id.Value}"));
    }

    /// <inheritdoc />
    public Task<RequestResult> RequestAsync(
        ExternalId id,
        MediaType mediaType,
        IReadOnlyList<int>? seasons,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        cancellationToken.ThrowIfCancellationRequested();

        if (mediaType == MediaType.Movie && seasons is { Count: > 0 })
        {
            throw new ProviderException(
                ProviderErrorCode.InvalidRequest, "Seasons cannot be requested for a movie.");
        }

        var wasNew = _requested.TryAdd((id.Source, id.Value, mediaType), 0);

        _logger.LogInformation(
            "Fake provider accepted a request for {Id} ({MediaType}). New: {WasNew}.",
            id,
            mediaType,
            wasNew);

        // Accepted either way. Re-requesting something already requested is not
        // an error: a caller cannot reliably prevent a double submission from a
        // remote control, and a duplicate is harmless.
        return Task.FromResult(
            new RequestResult(true, Describe(id, mediaType, $"Fake {mediaType} {id.Value}")));
    }

    private RequestItem Describe(ExternalId id, MediaType mediaType, string title)
    {
        var state = _requested.ContainsKey((id.Source, id.Value, mediaType))
            ? RequestState.Requested
            : RequestState.Requestable;

        return new RequestItem([id], mediaType, title, state)
        {
            Overview = "Placeholder item from the fake request provider. Nothing will be fulfilled.",
        };
    }
}
