namespace Jellyfin.Plugin.RequestBridge.Configuration;

/// <summary>
/// Supplies the plugin's current configuration.
/// </summary>
/// <remarks>
/// <para>
/// Exists so that components needing configuration do not reach for
/// <see cref="Plugin.Instance"/> themselves. That static is null during service
/// registration and for the whole of any test, which made anything reading it
/// impossible to exercise without a running server.
/// </para>
/// <para>
/// Read on every access rather than captured, because an administrator may
/// change settings at any time.
/// </para>
/// </remarks>
public interface IPluginConfigurationSource
{
    /// <summary>
    /// Gets the current configuration, or null when the plugin is not yet constructed.
    /// </summary>
    PluginConfiguration? Current { get; }
}

/// <summary>
/// The production source, backed by the plugin singleton.
/// </summary>
public sealed class PluginConfigurationSource : IPluginConfigurationSource
{
    /// <inheritdoc />
    public PluginConfiguration? Current => Plugin.Instance?.Configuration;
}
