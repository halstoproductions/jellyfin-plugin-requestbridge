using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Providers.Seerr;

/// <summary>
/// Translates Seerr's two status concepts into a single RequestBridge state.
/// </summary>
/// <remarks>
/// <para>
/// Seerr describes an item with two separate values: how available the media is,
/// and whether a request for it was approved. Neither alone is sufficient. A
/// request can be approved while its media is still processing, and media can be
/// available with no request behind it at all.
/// </para>
/// <para>
/// Isolated from the provider so the mapping can be read, reasoned about, and
/// eventually tested without an HTTP client in the way. It is the one piece of
/// Seerr knowledge that is pure logic.
/// </para>
/// </remarks>
internal static class SeerrStateMapper
{
    /// <summary>
    /// Maps a Seerr availability block onto a RequestBridge state.
    /// </summary>
    /// <param name="mediaInfo">
    /// The availability block, or null when Seerr knows the title but has no
    /// record for it, which is the ordinary case for something never requested.
    /// </param>
    /// <returns>The corresponding state.</returns>
    public static RequestState ToRequestState(SeerrMediaInfo? mediaInfo)
    {
        // No media record at all: Seerr knows the title from its catalogue but
        // nobody has asked for it. That is precisely "requestable".
        if (mediaInfo is null)
        {
            return RequestState.Requestable;
        }

        var mediaStatus = (SeerrMediaStatus)mediaInfo.Status;

        // A declined request leaves the item askable again. Surfacing "declined"
        // would leak an approval workflow through an abstraction that
        // deliberately does not model approval.
        var hasOpenRequest = mediaInfo.Requests?.Any(request =>
            (SeerrRequestStatus)request.Status is SeerrRequestStatus.PendingApproval or SeerrRequestStatus.Approved)
            ?? false;

        return mediaStatus switch
        {
            SeerrMediaStatus.Available => RequestState.Available,
            SeerrMediaStatus.PartiallyAvailable => RequestState.PartiallyAvailable,
            SeerrMediaStatus.Processing => RequestState.Processing,

            // Pending means requested but not yet started.
            SeerrMediaStatus.Pending => RequestState.Requested,

            // Deleted means the record went away, so it can be asked for again.
            SeerrMediaStatus.Deleted => RequestState.Requestable,

            // Unknown media status, but an open request exists: the request is
            // the more reliable signal.
            SeerrMediaStatus.Unknown when hasOpenRequest => RequestState.Requested,

            SeerrMediaStatus.Unknown => RequestState.Requestable,

            // A status this mapper does not recognise. Deliberately Unknown
            // rather than a guess: a newer Seerr adding a value must not cause
            // this to claim something it cannot support.
            _ => RequestState.Unknown,
        };
    }
}
