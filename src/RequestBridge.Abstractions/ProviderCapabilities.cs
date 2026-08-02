namespace RequestBridge.Abstractions;

/// <summary>
/// What a provider can do, so that a caller can adapt to it without knowing what
/// it is.
/// </summary>
/// <remarks>
/// <para>
/// The governing rule: a caller branches on what a provider <b>can do</b>, never
/// on what a provider <b>is</b>. Capability flags exist so that provider identity
/// never needs to be inspected.
/// </para>
/// <para>
/// Every flag must have a defined behaviour when false, so that a partial
/// provider is usable rather than broken. The defined behaviours are in
/// <c>docs/capabilities.md</c>.
/// </para>
/// </remarks>
public sealed record ProviderCapabilities
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderCapabilities"/> class.
    /// </summary>
    /// <param name="displayName">A human-readable provider name, for display only.</param>
    /// <param name="health">Whether the provider is currently usable.</param>
    /// <exception cref="ArgumentNullException"><paramref name="displayName"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="displayName"/> is empty or whitespace.
    /// </exception>
    public ProviderCapabilities(string displayName, ProviderHealth health)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException(
                "A provider display name must not be empty.", nameof(displayName));
        }

        DisplayName = displayName;
        Health = health;
    }

    /// <summary>
    /// Gets a human-readable provider name.
    /// </summary>
    /// <remarks>
    /// <b>For display only, in administrator-facing interfaces.</b> Branching on
    /// this value is an architectural violation: it reintroduces exactly the
    /// coupling that capability flags exist to prevent. If behaviour must vary,
    /// the correct fix is a new capability flag, never a comparison against this
    /// string.
    /// </remarks>
    public string DisplayName { get; }

    /// <summary>
    /// Gets whether the provider is currently usable.
    /// </summary>
    public ProviderHealth Health { get; init; }

    /// <summary>
    /// Gets the media types this provider handles. Empty means none.
    /// </summary>
    /// <remarks>
    /// A media type that is absent behaves, for that type, as though no provider
    /// were installed at all.
    /// </remarks>
    public IReadOnlyList<MediaType> SupportedMediaTypes { get; init; } = [];

    /// <summary>
    /// Gets the states this provider can actually emit.
    /// </summary>
    /// <remarks>
    /// Advertised so a caller knows which distinctions are real for this provider
    /// rather than assuming the full enum is populated. This is also what makes
    /// adding a state later a non-breaking change.
    /// </remarks>
    public IReadOnlyList<RequestState> SupportedStates { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether searching is supported.
    /// </summary>
    public bool CanSearch { get; init; }

    /// <summary>
    /// Gets a value indicating whether requesting is supported.
    /// </summary>
    public bool CanRequest { get; init; }

    /// <summary>
    /// Gets a value indicating whether reported state is meaningful.
    /// </summary>
    /// <remarks>
    /// When false, a caller should expect <see cref="RequestState.Unknown"/> and
    /// present no state at all.
    /// </remarks>
    public bool CanReportStatus { get; init; }

    /// <summary>
    /// Gets a value indicating whether requesting individual seasons is honoured.
    /// </summary>
    /// <remarks>
    /// When false, a caller must request a whole series and must not offer a
    /// season picker.
    /// </remarks>
    public bool SupportsSeasonSelection { get; init; }
}
