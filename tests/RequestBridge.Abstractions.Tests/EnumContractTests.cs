using RequestBridge.Abstractions;

namespace RequestBridge.Abstractions.Tests;

/// <summary>
/// Tests that pin down enum choices the rest of the system depends on.
/// </summary>
/// <remarks>
/// These read like tests of the language rather than of behaviour, and that is
/// the point. Each one encodes a decision that is invisible at the call site and
/// would be easy to undo by adding a member in the wrong place.
/// </remarks>
public class EnumContractTests
{
    [Fact]
    public void RequestState_UnknownIsDefault()
    {
        // Unknown is the only safe default: when nothing is known, the honest
        // answer is that nothing is known.
        Assert.Equal(RequestState.Unknown, default);
    }

    [Fact]
    public void MediaType_HasNoValidDefault()
    {
        // Starting at 1 makes an unset media type fail loudly instead of
        // silently meaning Movie.
        Assert.False(Enum.IsDefined(default(MediaType)));
    }

    [Fact]
    public void ExternalIdSource_HasNoValidDefault()
    {
        Assert.False(Enum.IsDefined(default(ExternalIdSource)));
    }

    [Fact]
    public void ProviderHealth_NotConfiguredIsDefault()
    {
        // A provider that has not reported anything has not been configured, so
        // the default must not read as healthy.
        Assert.Equal(ProviderHealth.NotConfigured, default);
    }

    [Fact]
    public void ProviderErrorCode_UnknownIsDefault()
    {
        Assert.Equal(ProviderErrorCode.Unknown, default);
    }

    [Fact]
    public void RequestState_ContainsOnlyDeterminableStates()
    {
        // Searching, downloading, and importing were removed at Milestone 4
        // because no provider can distinguish them. This test exists so that
        // re-adding one is a deliberate act rather than an accident.
        var states = Enum.GetNames<RequestState>();

        Assert.Equal(
            ["Unknown", "Requestable", "Requested", "Processing", "PartiallyAvailable", "Available"],
            states);
    }
}
