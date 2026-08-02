namespace RequestBridge.Abstractions;

/// <summary>
/// The kind of media an item represents.
/// </summary>
/// <remarks>
/// Values deliberately start at one, so that <c>default(MediaType)</c> is not a
/// valid media type. An unset media type is a programming error and should fail
/// loudly rather than silently mean "movie".
/// </remarks>
public enum MediaType
{
    /// <summary>
    /// A single film.
    /// </summary>
    Movie = 1,

    /// <summary>
    /// An episodic series, which may be requested in whole or by season.
    /// </summary>
    Series = 2,
}
