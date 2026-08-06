using Jellyfin.Plugin.RequestBridge.Api;
using Microsoft.AspNetCore.Http;
using RequestBridge.Abstractions;

namespace Jellyfin.Plugin.RequestBridge.Tests;

/// <summary>
/// Tests for the domain to wire translation.
/// </summary>
/// <remarks>
/// This mapping is the contract with shipped Android clients that cannot be
/// updated alongside the server, so its output shape matters more than most
/// code in this repository.
/// </remarks>
public class ApiMappingTests
{
    private static RequestItem Item(RequestState state = RequestState.Requestable) =>
        new([new ExternalId(ExternalIdSource.Tmdb, "27205")], MediaType.Movie, "Inception", state)
        {
            Year = 2010,
            Overview = "A thief who steals corporate secrets.",
            ImageUrl = new Uri("https://image.tmdb.org/t/p/w500/poster.jpg"),
        };

    [Fact]
    public void ToDto_SerialisesEnumsAsNames()
    {
        // Enums cross the wire as strings, converted explicitly rather than by a
        // serializer setting, so the contract cannot change because someone
        // adjusted the server's JSON options.
        var dto = ApiMapping.ToDto(Item());

        Assert.Equal("Movie", dto.MediaType);
        Assert.Equal("Requestable", dto.State);
        Assert.Equal("Tmdb", dto.ExternalIds[0].Source);
        Assert.Equal("27205", dto.ExternalIds[0].Value);
    }

    [Fact]
    public void ToDto_CarriesOptionalFields()
    {
        var dto = ApiMapping.ToDto(Item());

        Assert.Equal(2010, dto.Year);
        Assert.Equal("A thief who steals corporate secrets.", dto.Overview);
        Assert.Equal("https://image.tmdb.org/t/p/w500/poster.jpg", dto.ImageUrl);
    }

    [Fact]
    public void ToDto_OmitsLibraryIdUnlessSupplied()
    {
        // A provider cannot know the library id, so it is absent unless the host
        // supplies one. This is the difference between the wire model and the
        // domain model.
        Assert.Null(ApiMapping.ToDto(Item()).JellyfinItemId);
        Assert.Equal("abc123", ApiMapping.ToDto(Item(), "abc123").JellyfinItemId);
    }

    [Theory]
    [InlineData("Tmdb", ExternalIdSource.Tmdb)]
    [InlineData("tmdb", ExternalIdSource.Tmdb)]
    [InlineData("TVDB", ExternalIdSource.Tvdb)]
    public void TryParseSource_IsCaseInsensitive(string input, ExternalIdSource expected)
    {
        Assert.True(ApiMapping.TryParseSource(input, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("Netflix")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("0")]
    public void TryParseSource_RejectsUnknown(string? input)
    {
        // "0" matters: Enum.TryParse accepts numeric strings, so a caller could
        // otherwise smuggle in an undefined value.
        Assert.False(ApiMapping.TryParseSource(input, out _));
    }

    [Theory]
    [InlineData("Movie", MediaType.Movie)]
    [InlineData("series", MediaType.Series)]
    public void TryParseMediaType_IsCaseInsensitive(string input, MediaType expected)
    {
        Assert.True(ApiMapping.TryParseMediaType(input, out var parsed));
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("Album")]
    [InlineData("0")]
    [InlineData(null)]
    public void TryParseMediaType_RejectsUnknown(string? input)
    {
        Assert.False(ApiMapping.TryParseMediaType(input, out _));
    }

    [Theory]
    [InlineData(ProviderErrorCode.ProviderNotConfigured, StatusCodes.Status503ServiceUnavailable)]
    [InlineData(ProviderErrorCode.ProviderUnreachable, StatusCodes.Status502BadGateway)]
    [InlineData(ProviderErrorCode.ProviderRejected, StatusCodes.Status502BadGateway)]
    [InlineData(ProviderErrorCode.NotSupported, StatusCodes.Status400BadRequest)]
    [InlineData(ProviderErrorCode.InvalidRequest, StatusCodes.Status400BadRequest)]
    [InlineData(ProviderErrorCode.ItemNotFound, StatusCodes.Status404NotFound)]
    [InlineData(ProviderErrorCode.RateLimited, StatusCodes.Status429TooManyRequests)]
    [InlineData(ProviderErrorCode.Unknown, StatusCodes.Status500InternalServerError)]
    public void ToStatusCode_MapsEveryErrorCode(ProviderErrorCode code, int expected)
    {
        Assert.Equal(expected, ApiMapping.ToStatusCode(code));
    }

    [Fact]
    public void ToStatusCode_HandlesEveryDefinedCode()
    {
        // A new error code that nobody mapped would silently become a 500. This
        // fails the moment one is added without a decision about its transport.
        foreach (var code in Enum.GetValues<ProviderErrorCode>())
        {
            var status = ApiMapping.ToStatusCode(code);

            if (code == ProviderErrorCode.Unknown)
            {
                Assert.Equal(StatusCodes.Status500InternalServerError, status);
            }
            else
            {
                Assert.NotEqual(StatusCodes.Status500InternalServerError, status);
            }
        }
    }
}
