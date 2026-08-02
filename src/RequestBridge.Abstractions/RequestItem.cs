namespace RequestBridge.Abstractions;

/// <summary>
/// A single piece of media as a provider describes it, together with its state.
/// </summary>
/// <remarks>
/// <para>
/// This type carries no media library identifier. A provider has no knowledge of
/// the host media library, and the common case for a requestable item is that no
/// library entry exists at all. Correlating an item with a library entry is the
/// host's responsibility, performed above this abstraction.
/// </para>
/// <para>
/// Immutable by construction. A provider must not be able to hand back an object
/// that its caller can mutate.
/// </para>
/// </remarks>
public sealed record RequestItem
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RequestItem"/> class.
    /// </summary>
    /// <param name="externalIds">
    /// The identifiers for this item. Must contain at least one entry, since an
    /// item that cannot be identified cannot be requested or tracked.
    /// </param>
    /// <param name="mediaType">The kind of media.</param>
    /// <param name="title">The display title.</param>
    /// <param name="state">The state of the item as the provider determined it.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="externalIds"/> or <paramref name="title"/> is null.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="externalIds"/> is empty, or <paramref name="title"/> is
    /// empty or whitespace.
    /// </exception>
    public RequestItem(
        IReadOnlyList<ExternalId> externalIds,
        MediaType mediaType,
        string title,
        RequestState state)
    {
        ArgumentNullException.ThrowIfNull(externalIds);
        ArgumentNullException.ThrowIfNull(title);

        if (externalIds.Count == 0)
        {
            throw new ArgumentException(
                "An item must carry at least one external id.", nameof(externalIds));
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("An item title must not be empty.", nameof(title));
        }

        ExternalIds = externalIds;
        MediaType = mediaType;
        Title = title;
        State = state;
    }

    /// <summary>
    /// Gets the identifiers for this item. Never empty.
    /// </summary>
    /// <remarks>
    /// An item may carry several, for example both a TMDB and a TVDB identifier
    /// for a series.
    /// </remarks>
    public IReadOnlyList<ExternalId> ExternalIds { get; }

    /// <summary>
    /// Gets the kind of media.
    /// </summary>
    public MediaType MediaType { get; }

    /// <summary>
    /// Gets the display title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the state of this item, as far as the provider could determine it.
    /// </summary>
    public RequestState State { get; init; }

    /// <summary>
    /// Gets the release year, or null when the provider does not know it.
    /// </summary>
    public int? Year { get; init; }

    /// <summary>
    /// Gets a short description, or null when the provider does not supply one.
    /// </summary>
    public string? Overview { get; init; }

    /// <summary>
    /// Gets an absolute URL to poster artwork, or null when none is available.
    /// </summary>
    /// <remarks>
    /// Absolute because the caller has no basis on which to resolve a relative
    /// URL: the artwork is hosted by the provider or its upstream catalogue, not
    /// by the host.
    /// </remarks>
    public Uri? ImageUrl { get; init; }

    /// <summary>
    /// Returns a diagnostic representation.
    /// </summary>
    /// <returns>A string identifying the title, year, and state.</returns>
    public override string ToString() =>
        Year is null ? $"{Title} [{State}]" : $"{Title} ({Year}) [{State}]";
}
