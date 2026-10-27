using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels;

[QueryProperty(nameof(ArtistId), "id")]
public partial class ArtistDetailViewModel(IPlaatClient plaatClient, PlayerQueueService queue, LoopbackProxyService proxy) : ViewModelBase
{
    private const int PopularTracksMinPlayCount = 5;
    private const int PopularTracksMax = 20;
    private const int PopularTracksCollapsedCount = 3;

    [ObservableProperty]
    public partial int ArtistId { get; set; }

    [ObservableProperty]
    public partial ArtistDetailsDto? Artist { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial string? BioText { get; set; }

    /// <summary>
    /// The loopback-proxy URL for the artist's Discogs image, or null while there isn't one. Kept
    /// as a property rather than converting <see cref="ArtistId"/> in XAML: on a first visit the
    /// image only appears once the fire-and-forget Discogs enrichment comes back, and an Image
    /// whose Source never changes won't retry a URL that 404'd before enrichment ran.
    /// </summary>
    [ObservableProperty]
    public partial string? ArtistImageUrl { get; set; }

    [ObservableProperty]
    public partial bool IsPopularTracksExpanded { get; set; }

    [ObservableProperty]
    public partial bool HasMorePopularTracks { get; set; }

    public ObservableCollection<AlbumOnlyDto> Albums { get; } = [];

    public ObservableCollection<TrackDto> Tracks { get; } = [];

    public ObservableCollection<TrackDto> PopularTracks { get; } = [];

    public ObservableCollection<TrackDto> DisplayedPopularTracks { get; } = [];

    public LoopbackProxyService Proxy
    {
        get
        {
            return proxy;
        }
    }

    partial void OnArtistIdChanged(int value)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Artist = await plaatClient.GetArtist(ArtistId, CancellationToken.None);
            Title = Artist?.Name ?? "Artist";

            Albums.Clear();
            Tracks.Clear();
            PopularTracks.Clear();
            IsPopularTracksExpanded = false;
            ArtistImageUrl = null;

            if (Artist is not null)
            {
                foreach (var album in Artist.Albums)
                {
                    Albums.Add(album);
                }

                foreach (var track in Artist.Tracks)
                {
                    Tracks.Add(track);
                }

                foreach (var track in Artist.Tracks
                             .Where(t => t.PlayCount > PopularTracksMinPlayCount)
                             .OrderByDescending(t => t.PlayCount)
                             .Take(PopularTracksMax))
                {
                    PopularTracks.Add(track);
                }

                HasMorePopularTracks = PopularTracks.Count > PopularTracksCollapsedCount;
                RefreshDisplayedPopularTracks();

                BioText = DiscogsSummary.ToPlainText(Artist.DiscogsSummary);
                ArtistImageUrl = BuildArtistImageUrl(Artist);

                if (Artist.DiscogsLookupStatus == ArtistDiscogsLookupStatus.NotAttempted)
                {
                    _ = EnrichAsync();
                }
            }

            IsFavorite = await plaatClient.IsArtistFavorite((uint)ArtistId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load artist: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnIsPopularTracksExpandedChanged(bool value)
    {
        RefreshDisplayedPopularTracks();
    }

    private void RefreshDisplayedPopularTracks()
    {
        DisplayedPopularTracks.Clear();

        var source = IsPopularTracksExpanded ? PopularTracks : PopularTracks.Take(PopularTracksCollapsedCount);

        foreach (var track in source)
        {
            DisplayedPopularTracks.Add(track);
        }
    }

    [RelayCommand]
    private void ToggleExpandPopularTracks()
    {
        IsPopularTracksExpanded = !IsPopularTracksExpanded;
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        try
        {
            var newValue = !IsFavorite;
            await plaatClient.SetArtistFavorite((uint)ArtistId, newValue, CancellationToken.None);
            IsFavorite = newValue;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't update favorite: {ex.Message}";
        }
    }

    private async Task EnrichAsync()
    {
        try
        {
            var enriched = await plaatClient.EnrichArtist((uint)ArtistId, CancellationToken.None);

            if (enriched is not null)
            {
                Artist = enriched;
                BioText = DiscogsSummary.ToPlainText(enriched.DiscogsSummary);
                ArtistImageUrl = BuildArtistImageUrl(enriched);
            }
        }
        catch
        {
            // best-effort enrichment; nothing to show the user if it fails.
        }
    }

    private string? BuildArtistImageUrl(ArtistDetailsDto artist)
    {
        return artist.HasDiscogsImage ? proxy.ArtistDiscogsImageUrl(artist.Id).ToString() : null;
    }

    public void PlayTrack(TrackDto track)
    {
        Play(Tracks, track);
    }

    /// <summary>
    /// Tapping a popular track queues the popular tracks themselves - including the ones still
    /// hidden behind "Show more" - rather than dropping the whole artist catalogue into the queue.
    /// </summary>
    public void PlayPopularTrack(TrackDto track)
    {
        Play(PopularTracks, track);
    }

    private void Play(ObservableCollection<TrackDto> tracks, TrackDto track)
    {
        var index = tracks.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        queue.SetPlayList(tracks);
        queue.Play(index);
    }
}
