using RequestBridge.Abstractions;

namespace RequestBridge.Abstractions.Tests;

/// <summary>
/// Tests for <see cref="ExternalId"/>, the identity of everything in this system.
/// </summary>
public class ExternalIdTests
{
    [Fact]
    public void Constructor_KeepsSourceAndValue()
    {
        var id = new ExternalId(ExternalIdSource.Tmdb, "603692");

        Assert.Equal(ExternalIdSource.Tmdb, id.Source);
        Assert.Equal("603692", id.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Constructor_RejectsEmptyValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new ExternalId(ExternalIdSource.Tmdb, value));
    }

    [Fact]
    public void Constructor_RejectsNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => new ExternalId(ExternalIdSource.Tmdb, null!));
    }

    [Fact]
    public void Constructor_RejectsUndefinedSource()
    {
        // default(ExternalIdSource) is deliberately not a valid member, so an
        // unset source must fail loudly rather than silently mean TMDB.
        Assert.Throws<ArgumentException>(() => new ExternalId(default, "1"));
    }

    [Fact]
    public void Equality_IsByValue()
    {
        // Identity is used as a lookup key across the client, the API, and the
        // provider. Reference equality here would break every one of them.
        var first = new ExternalId(ExternalIdSource.Tmdb, "27205");
        var second = new ExternalId(ExternalIdSource.Tmdb, "27205");

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equality_DistinguishesSource()
    {
        var tmdb = new ExternalId(ExternalIdSource.Tmdb, "27205");
        var tvdb = new ExternalId(ExternalIdSource.Tvdb, "27205");

        Assert.NotEqual(tmdb, tvdb);
    }

    [Fact]
    public void ToString_IsDiagnosticallyUseful()
    {
        Assert.Equal("Tmdb:603692", new ExternalId(ExternalIdSource.Tmdb, "603692").ToString());
    }
}
