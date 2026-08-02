namespace RequestBridge.Abstractions;

/// <summary>
/// A stable, machine-readable classification of a provider failure.
/// </summary>
/// <remarks>
/// <para>
/// Callers branch on these codes. They must never branch on exception messages,
/// which are for humans and are free to change.
/// </para>
/// <para>
/// Translating a code into a transport-level response is the host's job. A
/// provider classifies the failure; it does not decide how the failure is
/// presented.
/// </para>
/// </remarks>
public enum ProviderErrorCode
{
    /// <summary>
    /// The failure could not be classified.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// No provider is configured.
    /// </summary>
    ProviderNotConfigured = 1,

    /// <summary>
    /// The provider could not be reached.
    /// </summary>
    ProviderUnreachable = 2,

    /// <summary>
    /// The provider was reached and refused the operation.
    /// </summary>
    /// <remarks>
    /// Covers rejected credentials and insufficient provider-side permissions.
    /// Deliberately does not distinguish them, because the distinction is only
    /// actionable by an administrator reading logs, not by a caller.
    /// </remarks>
    ProviderRejected = 3,

    /// <summary>
    /// The operation is not supported by this provider.
    /// </summary>
    /// <remarks>
    /// A caller that respects <see cref="ProviderCapabilities"/> should never
    /// see this. It exists so that a provider can enforce its own advertised
    /// limits rather than trusting callers to.
    /// </remarks>
    NotSupported = 4,

    /// <summary>
    /// The operation was malformed, for example seasons supplied for a movie.
    /// </summary>
    InvalidRequest = 5,

    /// <summary>
    /// The provider does not know the requested item.
    /// </summary>
    /// <remarks>
    /// Only for operations where absence is a failure. A plain lookup returns
    /// null instead, because absence is an ordinary outcome there.
    /// </remarks>
    ItemNotFound = 6,

    /// <summary>
    /// The provider signalled that it is being called too often.
    /// </summary>
    RateLimited = 7,
}
