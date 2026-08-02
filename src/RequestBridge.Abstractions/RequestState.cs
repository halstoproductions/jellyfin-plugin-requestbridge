namespace RequestBridge.Abstractions;

/// <summary>
/// The state of an item, as far as it can actually be determined.
/// </summary>
/// <remarks>
/// <para>
/// Every value here exists because a provider can prove it from an observable
/// condition. States that cannot be proven are not in this enum, and must not be
/// added on the grounds that a provider might one day determine them.
/// </para>
/// <para>
/// No state is terminal. <see cref="Available"/> regresses to
/// <see cref="Requestable"/> if media is removed, so a caller must never cache a
/// state as final.
/// </para>
/// <para>
/// See <c>docs/state-machine.md</c> for transitions and provider mappings.
/// </para>
/// </remarks>
public enum RequestState
{
    /// <summary>
    /// Nothing can be said about this item: no usable identifier, no configured
    /// provider, the provider is unreachable, or the provider reports nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately zero. Unlike the other enums in this assembly, the default
    /// value here is meaningful and safe: when in doubt, nothing is known.
    /// </remarks>
    Unknown = 0,

    /// <summary>
    /// The provider knows this item and reports no active request for it.
    /// </summary>
    Requestable = 1,

    /// <summary>
    /// A request exists but fulfilment has not begun.
    /// </summary>
    Requested = 2,

    /// <summary>
    /// Fulfilment is under way, without any commitment to which stage.
    /// </summary>
    /// <remarks>
    /// This state is deliberately coarse. No provider currently distinguishes
    /// searching from downloading from importing, so RequestBridge does not
    /// pretend to. Callers must not render progress detail for this state.
    /// </remarks>
    Processing = 3,

    /// <summary>
    /// Some of the item is available and more is expected. Applies to series.
    /// </summary>
    PartiallyAvailable = 4,

    /// <summary>
    /// The item is fully available.
    /// </summary>
    Available = 5,
}
