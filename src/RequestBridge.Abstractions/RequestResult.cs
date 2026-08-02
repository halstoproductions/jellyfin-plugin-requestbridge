namespace RequestBridge.Abstractions;

/// <summary>
/// The outcome of asking a provider for an item.
/// </summary>
/// <param name="Accepted">
/// Whether the provider accepted the request. Asking for an item that is already
/// requested, in progress, or available counts as accepted: the caller's intent
/// is satisfied either way, and a duplicate request is harmless.
/// </param>
/// <param name="Item">
/// The item after the request, carrying its resulting state. Returned so that a
/// caller does not need an immediate follow-up lookup.
/// </param>
/// <remarks>
/// There is no approval outcome here. Whether a provider requires approval is its
/// own workflow, and surfacing it would leak provider behaviour through an
/// abstraction that deliberately does not model approval. A declined request
/// simply leaves the item <see cref="RequestState.Requestable"/>.
/// </remarks>
public sealed record RequestResult(bool Accepted, RequestItem Item);
