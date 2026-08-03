using System.Net.Mime;
using Jellyfin.Plugin.RequestBridge.Api.Dto;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
/// </remarks>
[ApiController]
[Authorize]
[Route("RequestBridge")]
[Produces(MediaTypeNames.Application.Json)]
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

    private readonly ILogger<RequestBridgeController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestBridgeController"/> class.
    /// </summary>
    /// <param name="logger">Logger for this controller.</param>
    public RequestBridgeController(ILogger<RequestBridgeController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Reports whether RequestBridge is installed and whether a provider exists.
    /// </summary>
    /// <returns>The current health of the plugin.</returns>
    /// <response code="200">RequestBridge is installed.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <remarks>
    /// Answers without contacting any provider. If reaching a provider were
    /// required, a provider outage would be indistinguishable from the plugin not
    /// being installed, and a client would permanently disable a feature that was
    /// only briefly unavailable.
    /// </remarks>
    [HttpGet("Health")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<HealthDto> GetHealth()
    {
        var version = Plugin.Instance?.Version?.ToString() ?? "unknown";

        _logger.LogDebug("RequestBridge health requested. Plugin version {Version}.", version);

        return new HealthDto(
            ApiVersion: ContractVersion,
            PluginVersion: version,
            ProviderConfigured: false);
    }

    /// <summary>
    /// Confirms that routing and authentication reach this plugin.
    /// </summary>
    /// <returns>A fixed acknowledgement.</returns>
    /// <response code="200">The request was routed and authenticated.</response>
    /// <response code="401">The caller is not authenticated.</response>
    /// <remarks>
    /// Exists to prove the plumbing during Milestone 6 and carries no product
    /// meaning. It returns a constant deliberately: an endpoint used to verify
    /// transport should not itself depend on anything that can fail.
    /// </remarks>
    [HttpGet("Ping")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> GetPing() => "RequestBridge";
}
