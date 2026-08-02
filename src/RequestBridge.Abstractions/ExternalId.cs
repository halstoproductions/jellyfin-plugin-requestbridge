namespace RequestBridge.Abstractions;

/// <summary>
/// An identifier for an item in an external catalogue.
/// </summary>
/// <remarks>
/// <para>
/// External identifiers are how RequestBridge identifies media. An item that can
/// be requested is by definition absent from the media library, so it has no
/// library identifier to be keyed on.
/// </para>
/// <para>
/// Equality is by value, so two instances with the same source and value are
/// interchangeable as lookup keys.
/// </para>
/// </remarks>
public sealed record ExternalId
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalId"/> class.
    /// </summary>
    /// <param name="source">The catalogue the identifier belongs to.</param>
    /// <param name="value">The identifier itself. Must not be empty or whitespace.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="value"/> is empty or whitespace, or <paramref name="source"/>
    /// is not a defined <see cref="ExternalIdSource"/>.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public ExternalId(ExternalIdSource source, string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("An external id value must not be empty.", nameof(value));
        }

        if (!Enum.IsDefined(source))
        {
            throw new ArgumentException($"Unknown external id source: {source}.", nameof(source));
        }

        Source = source;
        Value = value;
    }

    /// <summary>
    /// Gets the catalogue this identifier belongs to.
    /// </summary>
    public ExternalIdSource Source { get; }

    /// <summary>
    /// Gets the identifier itself, as the catalogue expresses it.
    /// </summary>
    /// <remarks>
    /// Held as a string rather than a number deliberately. Not every catalogue
    /// uses numeric identifiers, and a value that is never arithmetic should not
    /// be stored as though it were.
    /// </remarks>
    public string Value { get; }

    /// <summary>
    /// Returns a diagnostic representation, for example <c>Tmdb:603692</c>.
    /// </summary>
    /// <returns>A string identifying the source and value.</returns>
    public override string ToString() => $"{Source}:{Value}";
}
