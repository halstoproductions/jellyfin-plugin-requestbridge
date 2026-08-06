using Jellyfin.Plugin.RequestBridge.Configuration;
using Jellyfin.Plugin.RequestBridge.Library;
using Jellyfin.Plugin.RequestBridge.Providers.Seerr;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge;

/// <summary>
/// Registers RequestBridge services with the Jellyfin container.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam of the whole system. Swapping one provider for another is a
/// change to the single registration below and nothing else: the controller, the
/// HTTP contract, and every client stay untouched.
/// </para>
/// <para>
/// If replacing a provider ever requires editing a second file, the abstraction
/// has failed, and the correct response is to restore it rather than work around
/// it.
/// </para>
/// <para>
/// Instantiated by the server with <c>Activator.CreateInstance</c>, so it must
/// keep a parameterless constructor. It also runs <b>before</b> plugin instances
/// are constructed, so it must not touch <see cref="Plugin.Instance"/>, which is
/// still null at this point.
/// </para>
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // Configuration is resolved through an interface so that nothing needing
        // it has to reach for the plugin singleton, which is null during this
        // very method and throughout any test.
        serviceCollection.AddSingleton<IPluginConfigurationSource, PluginConfigurationSource>();

        // The provider swap. Milestone 7 registered FakeRequestProvider here and
        // nothing else changed when this line did.
        serviceCollection.AddSingleton<IRequestProvider, SeerrRequestProvider>();

        // Host-side concern, not a provider concern: whether this server already
        // holds an item is a question no provider can answer.
        serviceCollection.AddSingleton<LibraryPresenceResolver>();
    }
}
