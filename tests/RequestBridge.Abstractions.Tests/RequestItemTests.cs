using RequestBridge.Abstractions;

namespace RequestBridge.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="RequestItem"/>.
/// </summary>
public class RequestItemTests
{
    private static ExternalId Tmdb(string value = "27205") => new(ExternalIdSource.Tmdb, value);

    [Fact]
    public void Constructor_KeepsRequiredValues()
    {
        var item = new RequestItem([Tmdb()], MediaType.Movie, "Inception", RequestState.Requestable);

        Assert.Single(item.ExternalIds);
        Assert.Equal(MediaType.Movie, item.MediaType);
        Assert.Equal("Inception", item.Title);
        Assert.Equal(RequestState.Requestable, item.State);
    }

    [Fact]
    public void Constructor_RejectsEmptyExternalIds()
    {
        // An item that cannot be identified cannot be requested or tracked, so
        // there is no useful thing to do with one.
        Assert.Throws<ArgumentException>(() =>
            new RequestItem([], MediaType.Movie, "Inception", RequestState.Requestable));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_RejectsEmptyTitle(string title)
    {
        Assert.Throws<ArgumentException>(() =>
            new RequestItem([Tmdb()], MediaType.Movie, title, RequestState.Requestable));
    }

    [Fact]
    public void OptionalFields_DefaultToAbsent()
    {
        var item = new RequestItem([Tmdb()], MediaType.Movie, "Inception", RequestState.Requestable);

        Assert.Null(item.Year);
        Assert.Null(item.Overview);
        Assert.Null(item.ImageUrl);
    }

    [Fact]
    public void State_CanBeReplacedWithoutMutating()
    {
        // The controller overlays library presence by producing a new item. If
        // `with` mutated the original, a provider's cached instance would be
        // silently rewritten.
        var original = new RequestItem([Tmdb()], MediaType.Movie, "Inception", RequestState.Requestable);

        var overlaid = original with { State = RequestState.Available };

        Assert.Equal(RequestState.Requestable, original.State);
        Assert.Equal(RequestState.Available, overlaid.State);
        Assert.Equal(original.Title, overlaid.Title);
    }

    [Fact]
    public void ToString_IncludesYearWhenKnown()
    {
        var withYear = new RequestItem([Tmdb()], MediaType.Movie, "Inception", RequestState.Requestable)
        {
            Year = 2010,
        };

        Assert.Equal("Inception (2010) [Requestable]", withYear.ToString());
        Assert.Equal(
            "Inception [Requestable]",
            new RequestItem([Tmdb()], MediaType.Movie, "Inception", RequestState.Requestable).ToString());
    }
}
