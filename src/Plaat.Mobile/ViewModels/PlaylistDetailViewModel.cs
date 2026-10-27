using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;
using Plaat.Mobile.Services.Player;

namespace Plaat.Mobile.ViewModels;

[QueryProperty(nameof(PlaylistName), "name")]
public partial class PlaylistDetailViewModel(IPlaatClient plaatClient, PlayerQueueService queue) : ViewModelBase
{
    private List<TrackDto> _allTracks = [];

    [ObservableProperty]
    public partial string PlaylistName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial PlaylistDetailsDto? Playlist { get; set; }

    public BulkObservableCollection<TrackDto> Tracks { get; } = [];

    /// <summary>
    /// Filters the displayed track list, mirroring the Home ("all tracks") view's search.
    /// </summary>
    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilter();
            }
        }
    } = string.Empty;

    partial void OnPlaylistNameChanged(string value)
    {
        _ = LoadAsync();
    }

    /// <summary>
    /// Re-fetches the playlist, for when the page reappears - tracks may have been added to it from
    /// another tab while this page sat on that tab's navigation stack.
    /// </summary>
    public Task ReloadAsync()
    {
        return LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (string.IsNullOrEmpty(PlaylistName) || IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            Playlist = await plaatClient.GetPlaylist(Uri.UnescapeDataString(PlaylistName), CancellationToken.None);
            Title = Playlist?.PlaylistName ?? "Playlist";

            List<TrackDto> tracks = Playlist is not null ? [.. Playlist.Tracks] : [];

            // Only rebuild the list when its contents actually changed: every reappearance reloads,
            // and a Reset would throw away the scroll position for nothing. Keeping the old
            // instances also keeps PlayTrack's IndexOf lookups valid for the rows on screen.
            if (!HasLoadedOnce || !tracks.Select(t => t.Id).SequenceEqual(_allTracks.Select(t => t.Id)))
            {
                _allTracks = tracks;
                ApplyFilter();
            }

            HasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load playlist: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(Search)
            ? _allTracks
            : [.. _allTracks
                .Select(t => (Item: t, Score: SearchScoring.SearchScore(Search, t.Title, t.AllArtists)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];

        Tracks.ReplaceAll(filtered);
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
        // Queue the whole playlist, not just the filtered view - tapping a search hit should start
        // that track with the rest of the playlist still lined up behind it, not strand the queue
        // at only the tracks that matched the filter.
        var index = _allTracks.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        queue.SetPlayList(_allTracks);
        queue.Play(index);
    }

    /// <summary>
    /// Removes a track already deleted server-side (via TrackActionsSheet) from the
    /// displayed list, without a full reload.
    /// </summary>
    public void RemoveTrackLocally(TrackDto track)
    {
        _allTracks.Remove(track);
        ApplyFilter();
    }
}
