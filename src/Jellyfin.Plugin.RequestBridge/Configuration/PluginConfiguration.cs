using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.RequestBridge.Configuration;

/// <summary>
/// Administrator-editable settings for RequestBridge.
/// </summary>
/// <remarks>
/// Persisted as XML by <see cref="MediaBrowser.Common.Plugins.BasePlugin{TConfigurationType}"/>.
/// Every property needs a public parameterless-constructible default, because a
/// missing or unreadable configuration file is silently replaced with a default
/// instance by the base class.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the base URL of the request provider.
    /// </summary>
    /// <remarks>
    /// Empty until an administrator configures one. Unused by the skeleton; the
    /// first provider that reads it arrives with the Seerr provider.
    /// </remarks>
    public string ProviderBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the API key used to authenticate against the provider.
    /// </summary>
    /// <remarks>
    /// Stored in the plugin's own configuration file, which lives in the server's
    /// configuration directory and is readable by anyone with access to it. This
    /// is the same handling every other Jellyfin plugin gives a provider secret,
    /// but it is worth knowing rather than assuming otherwise.
    /// </remarks>
    public string ProviderApiKey { get; set; } = string.Empty;
}
