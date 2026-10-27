using Microsoft.AspNetCore.Components;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Artist : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required ILogger<Artist> Logger { get; init; }

    [Parameter]
    public required int ArtistId { get; init; }

    private const int SummaryCollapseLineThreshold = 4;
    private const int SummaryCollapseLengthThreshold = 400;

    private const int PopularTracksMinPlayCount = 5;
    private const int PopularTracksMax = 20;
    private const int PopularTracksCollapsedCount = 3;

    private ArtistDetailsDto? _artist;
    private bool _summaryExpanded;
    private bool _popularTracksExpanded;
    private bool _loading = true;
    private bool _isFavorite;

    private IReadOnlyCollection<TrackDto> MusicFileList
    {
        get
        {
            return _artist?.Tracks ?? [];
        }
    }

    private IReadOnlyCollection<AlbumOnlyDto> AlbumList
    {
        get
        {
            return _artist?.Albums ?? [];
        }
    }

    private List<TrackDto> PopularTracks
    {
        get
        {
            return [.. (_artist?.Tracks ?? [])
        .Where(t => t.PlayCount > PopularTracksMinPlayCount)
        .OrderByDescending(t => t.PlayCount)
        .Take(PopularTracksMax)];
        }
    }

    private IReadOnlyCollection<TrackDto> DisplayedPopularTracks
    {
        get
        {
            return _popularTracksExpanded ? PopularTracks : [.. PopularTracks.Take(PopularTracksCollapsedCount)];
        }
    }

    // While pending, the header reserves the image slot so the layout doesn't jump once enrichment
    // fills it in.
    private bool IsEnrichmentPending
    {
        get
        {
            return _artist?.DiscogsLookupStatus == ArtistDiscogsLookupStatus.NotAttempted;
        }
    }

    // Plain text rendered with white-space: pre-line, so newline count is a rough-but-cheap proxy
    // for rendered line count - good enough to decide whether the 5-line clamp is likely to actually
    // be clipping anything.
    private bool ShowSummaryToggle
    {
        get
        {
            return _artist?.DiscogsSummary is { } summary
        && (summary.Length > SummaryCollapseLengthThreshold
            || summary.Count(c => c == '\n') > SummaryCollapseLineThreshold);
        }
    }

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        _artist = await PlaatClient.GetArtist(ArtistId, tokenSource.Token);
        _summaryExpanded = false;
        _popularTracksExpanded = false;

        _isFavorite = _artist is not null && await PlaatClient.IsArtistFavorite((uint)ArtistId, tokenSource.Token);

        _loading = false;

        if (IsEnrichmentPending)
        {
            _ = EnrichArtist(ArtistId);
        }
    }

    private async Task ToggleFavorite()
    {
        var newValue = !_isFavorite;

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        await PlaatClient.SetArtistFavorite((uint)ArtistId, newValue, tokenSource.Token);

        _isFavorite = newValue;
    }

    private async Task EnrichArtist(int artistId)
    {
        try
        {
            using var tokenSource = new CancellationTokenSource();
            tokenSource.CancelAfter(Timeouts.LongTimeout);

            var enriched = await PlaatClient.EnrichArtist((uint)artistId, tokenSource.Token);

            if (enriched is not null && artistId == ArtistId)
            {
                _artist = enriched;
                await InvokeAsync(StateHasChanged);
            }
        }
        catch (Exception ex)
        {
            // Fire-and-forget from OnParametersSetAsync - the artist page has already rendered
            // without enrichment, so a failure here just means it stays that way until next view.
            LogEnrichFailed(Logger, ex, artistId);
        }
    }

    private void ToggleSummary()
    {
        _summaryExpanded = !_summaryExpanded;
    }

    private void TogglePopularTracks()
    {
        _popularTracksExpanded = !_popularTracksExpanded;
    }

    private static string GetImg(ulong albumId)
    {
        return $"/proxy/cover/album/{albumId}";
    }

    private static string GetArtistImg(uint artistId)
    {
        return $"/proxy/discogs-image/artist/{artistId}";
    }

    [LoggerMessage(LogLevel.Warning, "Failed to enrich artist {ArtistId}")]
    private static partial void LogEnrichFailed(ILogger logger, Exception ex, int artistId);
}
