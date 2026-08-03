namespace Jellyfin.Plugin.RequestBridge.Api.Dto;

/// <summary>
/// Wire model for an identifier in an external catalogue.
/// </summary>
/// <param name="Source">The catalogue name, for example <c>Tmdb</c>.</param>
/// <param name="Value">The identifier within that catalogue.</param>
/// <remarks>
/// Enumerations cross the wire as strings, and the mapping is explicit rather
/// than delegated to a serializer setting. The wire format is a contract with
/// shipped clients, and it must not be able to change because someone adjusted
/// the server's JSON options.
/// </remarks>
public sealed record ExternalIdDto(string Source, string Value);

/// <summary>
/// Wire model for a single item and its state.
/// </summary>
/// <param name="ExternalIds">Identifiers for this item. Never empty.</param>
/// <param name="MediaType"><c>Movie</c> or <c>Series</c>.</param>
/// <param name="Title">Display title.</param>
/// <param name="State">One of the RequestBridge states.</param>
/// <param name="Year">Release year, or null.</param>
/// <param name="Overview">Short description, or null.</param>
/// <param name="ImageUrl">Absolute artwork URL, or null.</param>
/// <param name="JellyfinItemId">
/// The Jellyfin library item id when the server already has this item, otherwise
/// null. Absent for anything requestable, which is the common case.
/// </param>
/// <remarks>
/// This model is a superset of the domain model. <paramref name="JellyfinItemId"/>
/// exists only here, because a provider has no knowledge of the host library and
/// could not populate it.
/// </remarks>
public sealed record RequestItemDto(
    IReadOnlyList<ExternalIdDto> ExternalIds,
    string MediaType,
    string Title,
    string State,
    int? Year,
    string? Overview,
    string? ImageUrl,
    string? JellyfinItemId);

/// <summary>
/// Wire model describing what the configured provider can do.
/// </summary>
/// <param name="ApiVersion">Version of the RequestBridge HTTP contract.</param>
/// <param name="ProviderDisplayName">
/// Provider name, for administrator-facing display only. Branching on this value
/// is an architectural violation: it reintroduces exactly the coupling that
/// capability flags exist to prevent.
/// </param>
/// <param name="ProviderHealth">Whether the provider is currently usable.</param>
/// <param name="SupportedMediaTypes">Media types this provider handles.</param>
/// <param name="SupportedStates">States this provider can actually emit.</param>
/// <param name="CanSearch">Whether searching is supported.</param>
/// <param name="CanRequest">Whether requesting is supported.</param>
/// <param name="CanReportStatus">Whether reported state is meaningful.</param>
/// <param name="SupportsSeasonSelection">Whether requesting single seasons is honoured.</param>
public sealed record CapabilitiesDto(
    int ApiVersion,
    string ProviderDisplayName,
    string ProviderHealth,
    IReadOnlyList<string> SupportedMediaTypes,
    IReadOnlyList<string> SupportedStates,
    bool CanSearch,
    bool CanRequest,
    bool CanReportStatus,
    bool SupportsSeasonSelection);

/// <summary>
/// Wire model for creating a request.
/// </summary>
/// <remarks>
/// A mutable class rather than a record, because ASP.NET Core model binding
/// populates it from the request body.
/// </remarks>
public class CreateRequestDto
{
    /// <summary>
    /// Gets or sets the catalogue the identifier belongs to, for example <c>Tmdb</c>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Gets or sets the identifier within that catalogue.
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Gets or sets the media type, <c>Movie</c> or <c>Series</c>.
    /// </summary>
    public string? MediaType { get; set; }

    /// <summary>
    /// Gets or sets specific seasons, or null for a whole series.
    /// </summary>
    public IReadOnlyList<int>? Seasons { get; set; }
}

/// <summary>
/// Wire model for the outcome of a request.
/// </summary>
/// <param name="Accepted">Whether the provider accepted it.</param>
/// <param name="Item">The item and its resulting state.</param>
public sealed record RequestResultDto(bool Accepted, RequestItemDto Item);

/// <summary>
/// Wire model for a failure.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable classification. Callers branch on this.
/// </param>
/// <param name="Message">
/// Human-readable detail. Free to change, and never contains provider-specific
/// wording, since that would leak the provider through the abstraction.
/// </param>
public sealed record ProblemDto(string Code, string Message);
