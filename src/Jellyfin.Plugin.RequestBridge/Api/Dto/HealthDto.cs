namespace Jellyfin.Plugin.RequestBridge.Api.Dto;

/// <summary>
/// Wire model reporting whether RequestBridge is present and usable.
/// </summary>
/// <param name="ApiVersion">
/// Version of the RequestBridge HTTP contract. Incremented only on a breaking
/// change. This belongs to the contract, not to any provider.
/// </param>
/// <param name="PluginVersion">The plugin assembly version, for diagnostics only.</param>
/// <param name="ProviderConfigured">
/// Whether a request provider is available. False for the skeleton, which has no
/// provider at all.
/// </param>
/// <remarks>
/// A wire model, deliberately separate from the abstraction models. See
/// <c>docs/components.md</c> section 3 for why the two contracts are not fused.
/// </remarks>
public sealed record HealthDto(
    int ApiVersion,
    string PluginVersion,
    bool ProviderConfigured);
