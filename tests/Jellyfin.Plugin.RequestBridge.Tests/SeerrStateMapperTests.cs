using Jellyfin.Plugin.RequestBridge.Providers.Seerr;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// Tests for the translation from Seerr's two status concepts to one state.
/// </summary>
/// <remarks>
/// The highest-value tests in this repository. This mapping is the only place
/// Seerr's vocabulary becomes RequestBridge's, it was reasoned about at length
/// but never executed against anything other than a live server, and a mistake
/// here shows up as a user being offered the wrong action.
/// </remarks>
public class SeerrStateMapperTests
{
    private const int SeerrUnknown = 1;
    private const int SeerrPending = 2;
    private const int SeerrProcessing = 3;
    private const int SeerrPartiallyAvailable = 4;
    private const int SeerrAvailable = 5;
    private const int SeerrDeleted = 6;

    private const int RequestPendingApproval = 1;
    private const int RequestApproved = 2;
    private const int RequestDeclined = 3;

    [Fact]
    public void NoMediaRecord_IsRequestable()
    {
        // Seerr knows the title from its catalogue but nobody has asked for it.
        // That is exactly what requestable means.
        Assert.Equal(RequestState.Requestable, SeerrStateMapper.ToRequestState(null));
    }

    [Theory]
    [InlineData(SeerrAvailable, RequestState.Available)]
    [InlineData(SeerrPartiallyAvailable, RequestState.PartiallyAvailable)]
    [InlineData(SeerrProcessing, RequestState.Processing)]
    [InlineData(SeerrPending, RequestState.Requested)]
    public void MediaStatus_MapsDirectly(int seerrStatus, RequestState expected)
    {
        Assert.Equal(expected, SeerrStateMapper.ToRequestState(new SeerrMediaInfo { Status = seerrStatus }));
    }

    [Fact]
    public void Deleted_IsRequestableAgain()
    {
        // The record went away, so the item can be asked for again. Reporting a
        // deleted state would expose provider bookkeeping the abstraction does
        // not model.
        Assert.Equal(
            RequestState.Requestable,
            SeerrStateMapper.ToRequestState(new SeerrMediaInfo { Status = SeerrDeleted }));
    }

    [Fact]
    public void UnknownStatus_WithNoRequest_IsRequestable()
    {
        Assert.Equal(
            RequestState.Requestable,
            SeerrStateMapper.ToRequestState(new SeerrMediaInfo { Status = SeerrUnknown }));
    }

    [Theory]
    [InlineData(RequestPendingApproval)]
    [InlineData(RequestApproved)]
    public void UnknownStatus_WithOpenRequest_IsRequested(int requestStatus)
    {
        // When the media status says nothing useful, an open request is the more
        // reliable signal.
        var mediaInfo = new SeerrMediaInfo
        {
            Status = SeerrUnknown,
            Requests = [new SeerrRequest { Status = requestStatus }],
        };

        Assert.Equal(RequestState.Requested, SeerrStateMapper.ToRequestState(mediaInfo));
    }

    [Fact]
    public void DeclinedRequest_DoesNotCountAsOpen()
    {
        // A declined request leaves the item askable again. Surfacing "declined"
        // would leak an approval workflow through an abstraction that
        // deliberately does not model approval.
        var mediaInfo = new SeerrMediaInfo
        {
            Status = SeerrUnknown,
            Requests = [new SeerrRequest { Status = RequestDeclined }],
        };

        Assert.Equal(RequestState.Requestable, SeerrStateMapper.ToRequestState(mediaInfo));
    }

    [Fact]
    public void UnrecognisedStatus_IsUnknown()
    {
        // A newer Seerr adding a status must not cause this to claim something
        // it cannot support. Unknown is the honest answer.
        Assert.Equal(
            RequestState.Unknown,
            SeerrStateMapper.ToRequestState(new SeerrMediaInfo { Status = 99 }));
    }

    [Fact]
    public void AvailableWins_EvenWithAnOpenRequest()
    {
        // A request record can outlive fulfilment. Availability is the more
        // useful fact for a viewer.
        var mediaInfo = new SeerrMediaInfo
        {
            Status = SeerrAvailable,
            Requests = [new SeerrRequest { Status = RequestApproved }],
        };

        Assert.Equal(RequestState.Available, SeerrStateMapper.ToRequestState(mediaInfo));
    }

    [Fact]
    public void EveryMappedState_IsOneTheProviderAdvertises()
    {
        // Capabilities promise which states a provider can emit. A mapping that
        // produced something outside that list would be a lie no client could
        // detect.
        var advertised = new SeerrRequestProvider(
            new StubHttpClientFactory(),
            new StubLogger<SeerrRequestProvider>()).Capabilities.SupportedStates;

        int[] everySeerrStatus =
            [SeerrUnknown, SeerrPending, SeerrProcessing, SeerrPartiallyAvailable, SeerrAvailable, SeerrDeleted];

        foreach (var status in everySeerrStatus)
        {
            var mapped = SeerrStateMapper.ToRequestState(new SeerrMediaInfo { Status = status });
            Assert.Contains(mapped, advertised);
        }

        Assert.Contains(SeerrStateMapper.ToRequestState(null), advertised);
    }
}
