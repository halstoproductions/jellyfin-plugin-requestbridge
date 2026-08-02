namespace RequestBridge.Abstractions;

/// <summary>
/// A source of requestable media.
/// </summary>
/// <remarks>
/// <para>
/// This interface is the seam of the entire system. Everything above it is
/// provider-agnostic; everything a specific product requires lives below it in a
/// single implementation. Replacing one provider with another must be a change to
/// a single dependency registration, and nothing else. If it ever requires a
/// second edit elsewhere, this abstraction has failed and the correct response is
/// to restore it rather than work around it.
/// </para>
/// <para>
/// Implementations must not let their own exceptions escape. Every failure
/// crossing this boundary is a <see cref="ProviderException"/> carrying a
/// <see cref="ProviderErrorCode"/>.
/// </para>
/// <para>
/// Implementations are resolved as singletons and may be called concurrently, so
/// they must be thread-safe.
/// </para>
/// </remarks>
public interface IRequestProvider
{
    /// <summary>
    /// Gets what this provider can do, and whether it is currently usable.
    /// </summary>
    /// <remarks>
    /// A property rather than a method, and deliberately synchronous, so that a
    /// caller can discover the provider while the provider itself is unreachable.
    /// Reachability is reported through <see cref="ProviderCapabilities.Health"/>,
    /// not by this member failing. Implementations must not perform network calls
    /// here.
    /// </remarks>
    ProviderCapabilities Capabilities { get; }

    /// <summary>
    /// Finds items matching a free-text query, including items the media library
    /// does not have.
    /// </summary>
    /// <param name="query">The search text. Must not be empty.</param>
    /// <param name="mediaType">
    /// Restricts results to one kind of media, or null for all supported kinds.
    /// </param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Matching items, each carrying its state. Empty when nothing matches, which
    /// is an ordinary outcome rather than a failure.
    /// </returns>
    /// <remarks>
    /// Implementations must filter out anything that is not requestable media,
    /// such as people. Non-media results must never reach the caller.
    /// </remarks>
    /// <exception cref="ProviderException">The provider failed or refused.</exception>
    Task<IReadOnlyList<RequestItem>> SearchAsync(
        string query,
        MediaType? mediaType,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current state of a single item.
    /// </summary>
    /// <param name="id">The identifier to look up.</param>
    /// <param name="mediaType">
    /// The kind of media. Required, because the same identifier value can exist
    /// for both a film and a series in some catalogues.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The item, or null when the provider does not know it. Absence is an
    /// ordinary outcome and is not an exception.
    /// </returns>
    /// <exception cref="ProviderException">The provider failed or refused.</exception>
    Task<RequestItem?> GetItemAsync(
        ExternalId id,
        MediaType mediaType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Asks the provider for an item.
    /// </summary>
    /// <param name="id">The identifier to request.</param>
    /// <param name="mediaType">The kind of media.</param>
    /// <param name="seasons">
    /// Specific seasons for a series, or null for all of it. Must be null for a
    /// film. Ignored when the provider does not advertise
    /// <see cref="ProviderCapabilities.SupportsSeasonSelection"/>.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The outcome, including the item's resulting state.</returns>
    /// <remarks>
    /// Requesting something already requested, in progress, or available is not
    /// an error. It succeeds and reports the current state. A caller cannot
    /// reliably prevent a double submission from a remote control, and a
    /// duplicate request is harmless.
    /// </remarks>
    /// <exception cref="ProviderException">The provider failed or refused.</exception>
    Task<RequestResult> RequestAsync(
        ExternalId id,
        MediaType mediaType,
        IReadOnlyList<int>? seasons,
        CancellationToken cancellationToken);
}
