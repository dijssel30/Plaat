using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels;

[QueryProperty(nameof(AlbumId), "id")]
public partial class AlbumDetailViewModel(IPlaatClient plaatClient, PlayerQueueService queue, LoopbackProxyService proxy) : ViewModelBase
{
    [ObservableProperty]
    public partial int AlbumId { get; set; }

    [ObservableProperty]
    public partial AlbumDetailsDto? Album { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    [ObservableProperty]
    public partial uint? PrimaryArtistId { get; set; }

    /// <summary>
    /// The album's release year, or a "first-last" range when its tracks disagree. Null when no
    /// track on the album carries a year.
    /// </summary>
    [ObservableProperty]
    public partial string? YearLabel { get; set; }

    /// <summary>
    /// Flat, disc/track-ordered list - this is what playback queues.
    /// </summary>
    public ObservableCollection<TrackDto> Tracks { get; } = [];

    /// <summary>
    /// The same tracks split per disc, for the grouped list in the UI.
    /// </summary>
    public ObservableCollection<TrackDiscGroup> TrackGroups { get; } = [];

    public LoopbackProxyService Proxy
    {
        get
        {
            return proxy;
        }
    }

    partial void OnAlbumIdChanged(int value)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Album = await plaatClient.GetAlbum(AlbumId, CancellationToken.None);
            Title = Album?.Title ?? "Album";

            Tracks.Clear();
            TrackGroups.Clear();
            YearLabel = null;

            if (Album is not null)
            {
                foreach (var track in Album.Tracks.OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
                {
                    Tracks.Add(track);
                }

                foreach (var group in AlbumTrackGrouping.BuildDiscGroups(Tracks))
                {
                    TrackGroups.Add(group);
                }

                YearLabel = AlbumTrackGrouping.BuildYearLabel(Tracks);
                PrimaryArtistId = Album.Artists.FirstOrDefault()?.Id;
            }

            IsFavorite = await plaatClient.IsAlbumFavorite((uint)AlbumId, CancellationToken.None);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load album: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleFavorite()
    {
        try
        {
            var newValue = !IsFavorite;
            await plaatClient.SetAlbumFavorite((uint)AlbumId, newValue, CancellationToken.None);
            IsFavorite = newValue;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't update favorite: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Play()
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        queue.SetPlayList(Tracks);
        queue.Play(0);
    }

    [RelayCommand]
    private void Shuffle()
    {
        if (Tracks.Count == 0)
        {
            return;
        }

        queue.SetPlayList(Tracks);
        queue.IsShuffled = true;
        queue.Play(0);
    }

    public void PlayTrack(TrackDto track)
    {
        var index = Tracks.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        queue.SetPlayList(Tracks);
        queue.Play(index);
    }
}
