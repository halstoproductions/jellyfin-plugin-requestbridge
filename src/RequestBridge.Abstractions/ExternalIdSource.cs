namespace RequestBridge.Abstractions;

/// <summary>
/// The external catalogue an identifier belongs to.
/// </summary>
/// <remarks>
/// <para>
/// Values deliberately start at one, so that <c>default(ExternalIdSource)</c> is
/// not a valid source.
/// </para>
/// <para>
/// This enum exists so that no part of the system passes identifier sources
/// around as strings. Adding a source is a deliberate change here, not an
/// accident at a call site.
/// </para>
/// </remarks>
public enum ExternalIdSource
{
    /// <summary>
    /// The Movie Database. The primary identifier used throughout RequestBridge,
    /// because it is what providers request against and what media libraries most
    /// commonly record.
    /// </summary>
    Tmdb = 1,

    /// <summary>
    /// TheTVDB. Supplements <see cref="Tmdb"/> for series, which may need both.
    /// </summary>
    Tvdb = 2,

    /// <summary>
    /// IMDb.
    /// </summary>
    Imdb = 3,
}
