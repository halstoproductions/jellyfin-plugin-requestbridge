using Jellyfin.Plugin.RequestBridge.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.RequestBridge;

/// <summary>
/// The RequestBridge plugin.
/// </summary>
/// <remarks>
/// <para>
/// This type is Jellyfin's entry point. It supplies identity, configuration
/// persistence, and the administrator configuration page. It deliberately holds
/// no request logic: everything a provider does lives behind
/// <see cref="global::RequestBridge.Abstractions.IRequestProvider"/>.
/// </para>
/// <para>
/// Note the <c>global::</c> prefix. This assembly's root namespace,
/// <c>Jellyfin.Plugin.RequestBridge</c>, ends in the same segment as the
/// <c>RequestBridge.Abstractions</c> namespace, so an unqualified reference from
/// inside this namespace resolves relatively and fails. Using directives are
/// unaffected, because they sit outside the namespace declaration.
/// </para>
/// </remarks>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Server paths, supplied by Jellyfin.</param>
    /// <param name="xmlSerializer">Serializer used for configuration persistence.</param>
    /// <param name="logger">Logger for this plugin.</param>
    /// <remarks>
    /// Constructed by the server through <c>ActivatorUtilities.CreateInstance</c>,
    /// so any registered service may be injected here.
    /// </remarks>
    public Plugin(
        IApplicationPaths applicationPaths,
        IXmlSerializer xmlSerializer,
        ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        // Says nothing about providers on purpose. Provider registration happens
        // before this constructor runs, in PluginServiceRegistrator, and this
        // type has no view of it. A log line that guesses at another component's
        // state is a line that eventually lies.
        logger.LogInformation("RequestBridge {Version} loaded.", Version);
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    /// <remarks>
    /// Null until the server constructs the plugin. Anything reading this must
    /// tolerate null rather than assume construction has happened, because
    /// service registration runs before plugin construction.
    /// </remarks>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "RequestBridge";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("4f9294b3-3204-418f-8fee-82b700573a9b");

    /// <inheritdoc />
    public override string Description =>
        "Exposes a provider-agnostic media request API for Jellyfin clients.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
        };
    }
}
