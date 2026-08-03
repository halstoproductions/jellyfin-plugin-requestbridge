using Jellyfin.Plugin.RequestBridge.Api.Dto;
using Microsoft.AspNetCore.Http;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Api;

/// <summary>
/// Translation between the provider abstraction and the HTTP contract.
/// </summary>
/// <remarks>
/// <para>
/// Mechanical by design. It contains no decisions, so that the two contracts can
/// evolve independently: the provider interface serves .NET provider authors,
/// while the wire contract serves shipped clients that cannot be updated in
/// lockstep with the server.
/// </para>
/// <para>
/// Enumerations are converted here explicitly rather than by a serializer
/// setting, so the wire format cannot change as a side effect of server
/// configuration.
/// </para>
/// </remarks>
internal static class ApiMapping
{
    /// <summary>
    /// Converts an item to its wire form.
    /// </summary>
    /// <param name="item">The domain item.</param>
    /// <param name="jellyfinItemId">
    /// The library item id when the server already has this item, otherwise null.
    /// Supplied by the caller, because a provider cannot know it.
    /// </param>
    /// <returns>The wire model.</returns>
    public static RequestItemDto ToDto(RequestItem item, string? jellyfinItemId = null) =>
        new(
            ExternalIds: [.. item.ExternalIds.Select(id => new ExternalIdDto(id.Source.ToString(), id.Value))],
            MediaType: item.MediaType.ToString(),
            Title: item.Title,
            State: item.State.ToString(),
            Year: item.Year,
            Overview: item.Overview,
            ImageUrl: item.ImageUrl?.ToString(),
            JellyfinItemId: jellyfinItemId);

    /// <summary>
    /// Converts provider capabilities to their wire form.
    /// </summary>
    /// <param name="capabilities">The provider capabilities.</param>
    /// <param name="apiVersion">
    /// The HTTP contract version, which belongs to the contract rather than to
    /// any provider and is therefore supplied here.
    /// </param>
    /// <returns>The wire model.</returns>
    public static CapabilitiesDto ToDto(ProviderCapabilities capabilities, int apiVersion) =>
        new(
            ApiVersion: apiVersion,
            ProviderDisplayName: capabilities.DisplayName,
            ProviderHealth: capabilities.Health.ToString(),
            SupportedMediaTypes: [.. capabilities.SupportedMediaTypes.Select(t => t.ToString())],
            SupportedStates: [.. capabilities.SupportedStates.Select(s => s.ToString())],
            CanSearch: capabilities.CanSearch,
            CanRequest: capabilities.CanRequest,
            CanReportStatus: capabilities.CanReportStatus,
            SupportsSeasonSelection: capabilities.SupportsSeasonSelection);

    /// <summary>
    /// Parses an identifier source from the wire.
    /// </summary>
    /// <param name="value">The incoming string.</param>
    /// <param name="source">The parsed source.</param>
    /// <returns>True when the value names a known source.</returns>
    public static bool TryParseSource(string? value, out ExternalIdSource source) =>
        Enum.TryParse(value, ignoreCase: true, out source) && Enum.IsDefined(source);

    /// <summary>
    /// Parses a media type from the wire.
    /// </summary>
    /// <param name="value">The incoming string.</param>
    /// <param name="mediaType">The parsed media type.</param>
    /// <returns>True when the value names a known media type.</returns>
    public static bool TryParseMediaType(string? value, out MediaType mediaType) =>
        Enum.TryParse(value, ignoreCase: true, out mediaType) && Enum.IsDefined(mediaType);

    /// <summary>
    /// Maps a provider error to the HTTP status that represents it.
    /// </summary>
    /// <param name="code">The provider error classification.</param>
    /// <returns>The HTTP status code.</returns>
    /// <remarks>
    /// Transport mapping lives here rather than in a provider. A provider
    /// classifies its failure; it does not decide how that failure is presented.
    /// </remarks>
    public static int ToStatusCode(ProviderErrorCode code) => code switch
    {
        ProviderErrorCode.ProviderNotConfigured => StatusCodes.Status503ServiceUnavailable,
        ProviderErrorCode.ProviderUnreachable => StatusCodes.Status502BadGateway,
        ProviderErrorCode.ProviderRejected => StatusCodes.Status502BadGateway,
        ProviderErrorCode.NotSupported => StatusCodes.Status400BadRequest,
        ProviderErrorCode.InvalidRequest => StatusCodes.Status400BadRequest,
        ProviderErrorCode.ItemNotFound => StatusCodes.Status404NotFound,
        ProviderErrorCode.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError,
    };
}
