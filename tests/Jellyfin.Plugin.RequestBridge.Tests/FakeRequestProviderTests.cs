using Jellyfin.Plugin.RequestBridge.Providers;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// Tests for the fake provider, which is now a test double rather than a shipped
/// provider and must keep behaving predictably for anything built on it.
/// </summary>
public class FakeRequestProviderTests
{
    private static FakeRequestProvider Create() => new(new StubLogger<FakeRequestProvider>());

    private static ExternalId Tmdb(string value = "12345") => new(ExternalIdSource.Tmdb, value);

    [Fact]
    public async Task Item_IsRequestableBeforeAnyRequest()
    {
        var item = await Create().GetItemAsync(Tmdb(), MediaType.Movie, CancellationToken.None);

        Assert.NotNull(item);
        Assert.Equal(RequestState.Requestable, item.State);
    }

    [Fact]
    public async Task Item_IsRequestedAfterARequest()
    {
        var provider = Create();

        await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);
        var item = await provider.GetItemAsync(Tmdb(), MediaType.Movie, CancellationToken.None);

        Assert.Equal(RequestState.Requested, item!.State);
    }

    [Fact]
    public async Task Request_NeverAdvancesBeyondRequested()
    {
        // A fake that appeared to progress towards Available would be a fake
        // progress bar wearing a different hat, and would test the fake rather
        // than anything real.
        var provider = Create();

        for (var i = 0; i < 5; i++)
        {
            await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);
        }

        var item = await provider.GetItemAsync(Tmdb(), MediaType.Movie, CancellationToken.None);

        Assert.Equal(RequestState.Requested, item!.State);
    }

    [Fact]
    public async Task Request_IsAcceptedTwice()
    {
        var provider = Create();

        var first = await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);
        var second = await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);

        Assert.True(first.Accepted);
        Assert.True(second.Accepted);
    }

    [Fact]
    public async Task State_IsPerItem()
    {
        var provider = Create();

        await provider.RequestAsync(Tmdb("111"), MediaType.Movie, null, CancellationToken.None);
        var other = await provider.GetItemAsync(Tmdb("222"), MediaType.Movie, CancellationToken.None);

        Assert.Equal(RequestState.Requestable, other!.State);
    }

    [Fact]
    public async Task State_DistinguishesMediaType()
    {
        // The same catalogue id can name both a film and a series, so the key
        // has to include the type.
        var provider = Create();

        await provider.RequestAsync(Tmdb(), MediaType.Movie, null, CancellationToken.None);
        var series = await provider.GetItemAsync(Tmdb(), MediaType.Series, CancellationToken.None);

        Assert.Equal(RequestState.Requestable, series!.State);
    }

    [Fact]
    public async Task Title_SurvivesARequest()
    {
        // Regression: requesting a search result used to rename it, because the
        // provider named items from whatever the caller passed.
        var provider = Create();

        var found = await provider.SearchAsync("matrix", MediaType.Movie, 3, CancellationToken.None);
        var id = found[0].ExternalIds[0];

        var result = await provider.RequestAsync(id, MediaType.Movie, null, CancellationToken.None);

        Assert.Equal(found[0].Title, result.Item.Title);
    }

    [Fact]
    public async Task Search_IsStableForTheSameQuery()
    {
        var provider = Create();

        var first = await provider.SearchAsync("matrix", MediaType.Movie, 3, CancellationToken.None);
        var second = await provider.SearchAsync("matrix", MediaType.Movie, 3, CancellationToken.None);

        Assert.Equal(
            first.Select(item => item.ExternalIds[0]),
            second.Select(item => item.ExternalIds[0]));
    }

    [Fact]
    public async Task Search_RespectsLimit()
    {
        var results = await Create().SearchAsync("matrix", MediaType.Movie, 1, CancellationToken.None);

        Assert.Single(results);
    }

    [Fact]
    public async Task Request_RejectsSeasonsForAMovie()
    {
        var provider = Create();

        var error = await Assert.ThrowsAsync<ProviderException>(() =>
            provider.RequestAsync(Tmdb(), MediaType.Movie, [1], CancellationToken.None));

        Assert.Equal(ProviderErrorCode.InvalidRequest, error.ErrorCode);
    }

    [Fact]
    public void Capabilities_AdvertiseOnlyReachableStates()
    {
        // Advertising Processing or Available would be a lie a client could not
        // detect, since this provider can never produce them.
        var states = Create().Capabilities.SupportedStates;

        Assert.DoesNotContain(RequestState.Processing, states);
        Assert.DoesNotContain(RequestState.Available, states);
        Assert.DoesNotContain(RequestState.PartiallyAvailable, states);
    }

    [Fact]
    public void Capabilities_DoNotClaimSeasonSelection()
    {
        // Seasons are accepted and ignored, so claiming support would offer the
        // user a picker that does nothing.
        Assert.False(Create().Capabilities.SupportsSeasonSelection);
    }
}
