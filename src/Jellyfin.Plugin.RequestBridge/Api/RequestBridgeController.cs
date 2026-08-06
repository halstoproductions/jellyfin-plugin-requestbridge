using System.Globalization;
using System.Net.Mime;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.RequestBridge.Api.Dto;
using Jellyfin.Plugin.RequestBridge.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Api;

/// <summary>
/// The RequestBridge HTTP surface.
/// </summary>
/// <remarks>
/// <para>
/// Routed automatically: the server clears MVC application parts and adds each
/// plugin assembly explicitly, then calls <c>AddControllersAsServices</c>. A
/// controller class in a plugin assembly therefore needs no registration and
/// receives full constructor injection.
/// </para>
/// <para>
/// <see cref="AuthorizeAttribute"/> without a policy means any authenticated
/// user. This is deliberate and must stay that way: requiring elevation would
/// make the feature unusable by the ordinary users it exists for.
/// </para>
/// <para>
/// This controller holds no provider knowledge. It validates input, calls the
/// provider, maps models, and maps failures onto transport. Nothing here may
/// become aware of which provider is configured.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("RequestBridge")]
[Produces(
    MediaTypeNames.Application.Json,
    JsonDefaults.CamelCaseMediaType,
    JsonDefaults.PascalCaseMediaType)]
public class RequestBridgeController : ControllerBase
{
    /// <summary>
    /// Version of the RequestBridge HTTP contract.
    /// </summary>
    /// <remarks>
    /// Incremented only on a breaking change. Additive changes, such as a new
    /// field or a new state, do not increment it. Clients negotiate on this
    /// rather than on a version in the URL path.
    /// </remarks>
    public const int ContractVersion = 1;

    private const int DefaultSearchLimit = 20;
    private const int MaximumSearchLimit = 50;

    private readonly IRequestProvider _provider;
    private readonly LibraryPresenceResolver _libraryPresence;
    private readonly ILogger<RequestBridgeController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestBridgeController"/> class.
    /// </summary>
    /// <param name="provider">The configured request provider.</param>
    /// <param name="libraryPresence">Correlates provider items with the library.</param>
    /// <param name="logger">Logger for this controller.</param>
    public RequestBridgeController(
        IRequestProvider provider,
        LibraryPresenceResolver libraryPresence,
        ILogger<RequestBridgeController> logger)
    {
        _provider = provider;
        _libraryPresence = libraryPresence;
        _logger = logger;
    }

    /// <summary>
    /// Reports whether RequestBridge is installed and whether a provider exists.
    /// </summary>
    /// <returns>The current health of the plugin.</returns>
    /// <response code="200">RequestBridge is installed.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <remarks>
    /// <c>providerConfigured</c> is derived from the provider's own reported
    /// health rather than hardcoded. A constant would stay true after a future
    /// change made it false, which is the kind of health check that reports
    /// success right up until someone needs it.
    /// </remarks>
    [HttpGet("Health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<HealthDto> GetHealth() =>
        new HealthDto(
            ApiVersion: ContractVersion,
            PluginVersion: Plugin.Instance?.Version?.ToString() ?? "unknown",
            ProviderConfigured: _provider.Capabilities.Health != ProviderHealth.NotConfigured);

    /// <summary>
    /// Confirms that routing and authentication reach this plugin.
    /// </summary>
    /// <returns>A fixed acknowledgement.</returns>
    /// <response code="200">The request was routed and authenticated.</response>
    /// <response code="401">The caller is not authenticated.</response>
    [HttpGet("Ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> GetPing() => "RequestBridge";

    /// <summary>
    /// Describes what the configured provider can do.
    /// </summary>
    /// <returns>The capability document.</returns>
    /// <response code="200">A provider is configured.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <remarks>
    /// Answers without contacting the provider. If reaching the provider were
    /// required, a provider outage would be indistinguishable from the plugin not
    /// being installed, and a client would permanently disable a feature that was
    /// only briefly unavailable. Reachability is reported inside the document as
    /// <c>providerHealth</c>.
    /// </remarks>
    [HttpGet("Capabilities")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<CapabilitiesDto> GetCapabilities() =>
        ApiMapping.ToDto(_provider.Capabilities, ContractVersion);

    /// <summary>
    /// Finds items, including items this server's library does not have.
    /// </summary>
    /// <param name="query">Free text to search for.</param>
    /// <param name="mediaType">
    /// <c>Movie</c> or <c>Series</c>. Omit for both.
    /// </param>
    /// <param name="limit">Maximum results. Clamped to a sane range.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Matching items, each carrying its state.</returns>
    /// <response code="200">The search completed. An empty list is a valid result.</response>
    /// <response code="400">The media type was not recognised, or searching is unsupported.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <remarks>
    /// This is the discovery surface for requestable media. An item absent from
    /// the library has no Jellyfin identity at all, so it cannot be found through
    /// the ordinary item search, which is why this endpoint exists.
    /// </remarks>
    [HttpGet("Search")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SearchResultDto>> Search(
        [FromQuery] string query,
        [FromQuery] string? mediaType,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Problem(ProviderErrorCode.InvalidRequest, "A search query is required.");
        }

        if (!_provider.Capabilities.CanSearch)
        {
            return Problem(ProviderErrorCode.NotSupported, "This provider does not support searching.");
        }

        MediaType? parsedMediaType = null;
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            if (!ApiMapping.TryParseMediaType(mediaType, out var parsed))
            {
                return Problem(ProviderErrorCode.InvalidRequest, $"Unknown media type: {mediaType}.");
            }

            parsedMediaType = parsed;
        }

        try
        {
            var items = await _provider
                .SearchAsync(query, parsedMediaType, ClampLimit(limit), cancellationToken)
                .ConfigureAwait(false);

            return new SearchResultDto([.. items.Select(Correlate)]);
        }
        catch (ProviderException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Reads the current state of a single item.
    /// </summary>
    /// <param name="source">The identifier catalogue, for example <c>Tmdb</c>.</param>
    /// <param name="value">The identifier within that catalogue.</param>
    /// <param name="mediaType">
    /// <c>Movie</c> or <c>Series</c>. Required, because the same identifier value
    /// can exist for both in some catalogues.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The item and its state.</returns>
    /// <response code="200">The item was found.</response>
    /// <response code="400">The source or media type was not recognised.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">The provider does not know this item.</response>
    [HttpGet("Items/{source}/{value}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequestItemDto>> GetItem(
        [FromRoute] string source,
        [FromRoute] string value,
        [FromQuery] string mediaType,
        CancellationToken cancellationToken)
    {
        if (!ApiMapping.TryParseSource(source, out var parsedSource))
        {
            return Problem(ProviderErrorCode.InvalidRequest, $"Unknown external id source: {source}.");
        }

        if (!ApiMapping.TryParseMediaType(mediaType, out var parsedMediaType))
        {
            return Problem(ProviderErrorCode.InvalidRequest, $"Unknown media type: {mediaType}.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return Problem(ProviderErrorCode.InvalidRequest, "An external id value is required.");
        }

        try
        {
            var item = await _provider
                .GetItemAsync(new ExternalId(parsedSource, value), parsedMediaType, cancellationToken)
                .ConfigureAwait(false);

            if (item is null)
            {
                return Problem(ProviderErrorCode.ItemNotFound, "The provider does not know this item.");
            }

            return Correlate(item);
        }
        catch (ProviderException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Asks the provider for an item.
    /// </summary>
    /// <param name="request">The item to request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome, including the item's resulting state.</returns>
    /// <response code="200">The request was accepted.</response>
    /// <response code="400">The body was malformed.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <response code="404">The provider does not know this item.</response>
    /// <remarks>
    /// Requesting something already requested is not an error. It succeeds and
    /// reports the current state.
    /// </remarks>
    [HttpPost("Requests")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RequestResultDto>> CreateRequest(
        [FromBody] CreateRequestDto request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(ProviderErrorCode.InvalidRequest, "A request body is required.");
        }

        if (!ApiMapping.TryParseSource(request.Source, out var parsedSource))
        {
            return Problem(ProviderErrorCode.InvalidRequest, $"Unknown external id source: {request.Source}.");
        }

        if (!ApiMapping.TryParseMediaType(request.MediaType, out var parsedMediaType))
        {
            return Problem(ProviderErrorCode.InvalidRequest, $"Unknown media type: {request.MediaType}.");
        }

        if (string.IsNullOrWhiteSpace(request.Value))
        {
            return Problem(ProviderErrorCode.InvalidRequest, "An external id value is required.");
        }

        try
        {
            var result = await _provider
                .RequestAsync(
                    new ExternalId(parsedSource, request.Value),
                    parsedMediaType,
                    request.Seasons,
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "RequestBridge accepted a request for {Source}:{Value}.", parsedSource, request.Value);

            return new RequestResultDto(result.Accepted, Correlate(result.Item));
        }
        catch (ProviderException ex)
        {
            return Problem(ex);
        }
    }

    /// <summary>
    /// Overlays library presence onto a provider-supplied item.
    /// </summary>
    /// <remarks>
    /// The library is authoritative. If this server holds the media, the item is
    /// Available no matter what the provider believes, because the provider has
    /// no visibility of this library. Without this the user is offered a request
    /// for something they already own.
    /// </remarks>
    private RequestItemDto Correlate(RequestItem item)
    {
        var libraryItemId = _libraryPresence.FindLibraryItemId(item);

        if (libraryItemId is null)
        {
            return ApiMapping.ToDto(item);
        }

        return ApiMapping.ToDto(
            item with { State = RequestState.Available },
            libraryItemId.Value.ToString("N", CultureInfo.InvariantCulture));
    }

    private ObjectResult Problem(ProviderException exception)
    {
        // Logged at this boundary so a provider failure is diagnosable, while the
        // caller receives only the neutral code and message.
        _logger.LogWarning(exception, "Request provider failed with {Code}.", exception.ErrorCode);

        return Problem(exception.ErrorCode, exception.Message);
    }

    private ObjectResult Problem(ProviderErrorCode code, string message) =>
        StatusCode(
            ApiMapping.ToStatusCode(code),
            new ProblemDto(code.ToString(), message));

    /// <summary>
    /// Constrains a caller-supplied result limit.
    /// </summary>
    /// <remarks>
    /// Clamped rather than rejected. An out-of-range limit is a caller being
    /// careless, not a request that cannot be served, and failing it would give
    /// a client a way to turn a cosmetic mistake into a broken screen.
    /// </remarks>
    private static int ClampLimit(int? requested) =>
        Math.Clamp(requested ?? DefaultSearchLimit, 1, MaximumSearchLimit);
}
