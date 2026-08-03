using Jellyfin.Plugin.RequestBridge.Providers;
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
        // Milestone 11 changes exactly this line to the Seerr provider.
        serviceCollection.AddSingleton<IRequestProvider, FakeRequestProvider>();
    }
}
