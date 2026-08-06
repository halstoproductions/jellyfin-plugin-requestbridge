using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using RequestBridge.Abstractions;

// Both namespaces define MediaType and both are needed here. Aliased rather than
// resolved by import order, so the collision is visible at the use site.
using RequestMediaType = RequestBridge.Abstractions.MediaType;

namespace Jellyfin.Plugin.RequestBridge.Library;

/// <summary>
/// Decides whether the Jellyfin library already contains a provider-supplied item.
/// </summary>
/// <remarks>
/// <para>
/// A provider knows nothing about this server's library, so it cannot report
/// <see cref="RequestState.Available"/> for something already on disk. Without
/// this correlation a user is offered a request for media they already own,
/// which is the single most visible way this feature can embarrass itself.
/// </para>
/// <para>
/// This lives above the provider abstraction on purpose. Library presence is a
/// property of the host, not of the provider, and pushing it down would require
/// every provider to know about Jellyfin.
/// </para>
/// </remarks>
public class LibraryPresenceResolver
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryPresenceResolver> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryPresenceResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">The server's library.</param>
    /// <param name="logger">Logger for this resolver.</param>
    public LibraryPresenceResolver(
        ILibraryManager libraryManager,
        ILogger<LibraryPresenceResolver> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    /// <summary>
    /// Finds the library item matching a provider-supplied item, if any.
    /// </summary>
    /// <param name="item">The provider-supplied item.</param>
    /// <returns>
    /// The library item id, or null when the library does not have it.
    /// </returns>
    /// <remarks>
    /// Presence is evaluated server-wide rather than per user. Whether a
    /// particular user can see the item is a separate question from whether the
    /// server has it, and requesting something the server already holds would be
    /// wrong regardless of who is asking.
    /// </remarks>
    public Guid? FindLibraryItemId(RequestItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var itemKind = ToItemKind(item.MediaType);

        foreach (var externalId in item.ExternalIds)
        {
            var providerName = ToProviderName(externalId.Source);
            if (providerName is null)
            {
                continue;
            }

            try
            {
                var matches = _libraryManager.GetItemList(new InternalItemsQuery
                {
                    IncludeItemTypes = [itemKind],
                    HasAnyProviderId = new Dictionary<string, string>
                    {
                        [providerName] = externalId.Value,
                    },
                    Recursive = true,
                    Limit = 1,
                    DtoOptions = new DtoOptions(false),
                });

                if (matches.Count > 0)
                {
                    return matches[0].Id;
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types
            catch (Exception ex)
#pragma warning restore CA1031
            {
                // A library lookup failure must not fail the whole request. The
                // worst outcome of treating this as "not present" is offering a
                // request the user did not need, which beats a broken screen.
                _logger.LogWarning(
                    ex, "Library lookup failed for {Source}:{Value}.", externalId.Source, externalId.Value);
            }
        }

        return null;
    }

    private static BaseItemKind ToItemKind(RequestMediaType mediaType) => mediaType switch
    {
        RequestMediaType.Series => BaseItemKind.Series,
        _ => BaseItemKind.Movie,
    };

    /// <summary>
    /// Maps an abstraction id source onto Jellyfin's provider name.
    /// </summary>
    /// <remarks>
    /// Returns null for a source Jellyfin does not record, so an unmappable id is
    /// skipped rather than being matched against the wrong provider.
    /// </remarks>
    private static string? ToProviderName(ExternalIdSource source) => source switch
    {
        ExternalIdSource.Tmdb => MetadataProvider.Tmdb.ToString(),
        ExternalIdSource.Tvdb => MetadataProvider.Tvdb.ToString(),
        ExternalIdSource.Imdb => MetadataProvider.Imdb.ToString(),
        _ => null,
    };
}
