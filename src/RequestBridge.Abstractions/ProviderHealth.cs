namespace RequestBridge.Abstractions;

/// <summary>
/// Whether a provider is currently usable.
/// </summary>
/// <remarks>
/// Health is reported as data rather than as the success or failure of a call.
/// If discovering a provider required the provider to be reachable, then a
/// provider outage would be indistinguishable from a provider not existing, and
/// a caller would permanently disable a feature that was only briefly down.
/// </remarks>
public enum ProviderHealth
{
    /// <summary>
    /// No provider is configured. Not an error, and not worth retrying.
    /// </summary>
    NotConfigured = 0,

    /// <summary>
    /// The provider is answering normally.
    /// </summary>
    Healthy = 1,

    /// <summary>
    /// The provider is reachable, but some operations are failing.
    /// </summary>
    Degraded = 2,

    /// <summary>
    /// The provider is configured but not answering. Worth retrying later.
    /// </summary>
    Unreachable = 3,
}
