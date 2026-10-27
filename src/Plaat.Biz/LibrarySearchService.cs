using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;

namespace Plaat.Biz;

/// <summary>
/// Global library search. Runs entirely against the API's already-in-memory cached catalog
/// (<see cref="ITrackRepository.GetAll"/> etc.) using the exact same
/// <see cref="StringExtensions.NormalizeForSearch"/> the web/mobile clients used to run locally -
/// so search behaviour (diacritic folding, casing) is unchanged, but a keystroke now transfers a
/// capped result set instead of the whole library, and the normalization work happens once on the
/// server rather than once per circuit / per device.
/// </summary>
public sealed class LibrarySearchService(
    ITrackRepository trackRepository,
    IAlbumRepository albumRepository,
    IArtistRepository artistRepository)
{
    public async Task<SearchResultsDto> Search(string? query, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new SearchResultsDto();
        }

        var tracksTask = trackRepository.GetAll(ct);
        var albumsTask = albumRepository.GetAlbums(ct);
        var artistsTask = artistRepository.GetArtists(ct);

        await Task.WhenAll(tracksTask, albumsTask, artistsTask);

        return new SearchResultsDto
        {
            Artists = RankByRelevance(await artistsTask, a => SearchScoring.SearchScore(query, a.Name))
                .Take(take)
                .ToArray(),
            Albums = RankByRelevance(await albumsTask, a => SearchScoring.SearchScore(query, a.Title, a.AllArtists))
                .Take(take)
                .ToArray(),
            Tracks = RankByRelevance(await tracksTask, t => SearchScoring.SearchScore(query, t.Title, t.AllArtists, t.Album))
                .Take(take)
                .ToArray()
        };
    }

    private static IEnumerable<T> RankByRelevance<T>(IEnumerable<T> items, Func<T, int?> score)
    {
        return items
            .Select(item => (Item: item, Score: score(item)))
            .Where(x => x.Score is not null)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Item);
    }
}
