using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.RequestBridge.Providers.Seerr;

/// <summary>
/// Availability of media as Seerr reports it.
/// </summary>
/// <remarks>
/// Seerr has no value for searching, downloading, or importing. All three are
/// <see cref="Processing"/>, and no download detail exists anywhere in its API.
/// This is the evidence behind the RequestBridge state machine collapsing those
/// three states rather than exposing distinctions no provider can determine.
/// </remarks>
internal enum SeerrMediaStatus
{
    /// <summary>Nothing is known.</summary>
    Unknown = 1,

    /// <summary>Requested, awaiting approval or not yet started.</summary>
    Pending = 2,

    /// <summary>Being fulfilled, with no indication of which stage.</summary>
    Processing = 3,

    /// <summary>Some of it exists. Common for series.</summary>
    PartiallyAvailable = 4,

    /// <summary>Fully available.</summary>
    Available = 5,

    /// <summary>The media record was removed.</summary>
    Deleted = 6,
}

/// <summary>
/// Approval state of a request, which is separate from media availability.
/// </summary>
internal enum SeerrRequestStatus
{
    /// <summary>Awaiting an administrator decision.</summary>
    PendingApproval = 1,

    /// <summary>Approved and passed downstream.</summary>
    Approved = 2,

    /// <summary>Refused.</summary>
    Declined = 3,
}

/// <summary>
/// Availability block attached to a search result or detail response.
/// </summary>
internal sealed class SeerrMediaInfo
{
    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("requests")]
    public IReadOnlyList<SeerrRequest>? Requests { get; set; }
}

/// <summary>
/// A single request record.
/// </summary>
internal sealed class SeerrRequest
{
    [JsonPropertyName("status")]
    public int Status { get; set; }
}

/// <summary>
/// One entry from a search response. Movies and series share a shape, differing
/// only in which title and date field is populated.
/// </summary>
internal sealed class SeerrSearchResult
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    [JsonPropertyName("posterPath")]
    public string? PosterPath { get; set; }

    [JsonPropertyName("releaseDate")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("firstAirDate")]
    public string? FirstAirDate { get; set; }

    [JsonPropertyName("mediaInfo")]
    public SeerrMediaInfo? MediaInfo { get; set; }
}

/// <summary>
/// A page of search results.
/// </summary>
internal sealed class SeerrSearchResponse
{
    [JsonPropertyName("results")]
    public IReadOnlyList<SeerrSearchResult>? Results { get; set; }
}

/// <summary>
/// Body for creating a request.
/// </summary>
internal sealed class SeerrCreateRequest
{
    [JsonPropertyName("mediaType")]
    public string MediaType { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the TMDB id. Seerr requests against TMDB, not its own ids.
    /// </summary>
    [JsonPropertyName("mediaId")]
    public int MediaId { get; set; }

    /// <summary>
    /// Gets or sets the seasons to request, for series only.
    /// </summary>
    /// <remarks>
    /// Omitted entirely when null rather than serialised as an explicit null.
    /// Seerr expects this key to be absent or an array, and rejects a null.
    /// </remarks>
    [JsonPropertyName("seasons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<int>? Seasons { get; set; }
}
