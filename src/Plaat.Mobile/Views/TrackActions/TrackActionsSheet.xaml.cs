using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Player;

namespace Plaat.Mobile.Views.TrackActions;

public sealed class SheetActionItem(string label, Func<Task> execute)
{
    public string Label { get; } = label;

    public Func<Task> Execute { get; } = execute;
}

public partial class TrackActionsSheet : ContentView
{
    private readonly ObservableCollection<SheetActionItem> _actions = [];
    private readonly ILogger<TrackActionsSheet> _logger = ServiceHelper.GetRequiredService<ILogger<TrackActionsSheet>>();

    private TrackDto? _track;
    private uint? _playlistId;

    /// <summary>
    /// Raised after a track is successfully removed from the playlist passed to
    /// <see cref="ShowAsync"/>, so the host page (which owns the actual track list) can update it.
    /// </summary>
    public event EventHandler<TrackDto>? RemovedFromPlaylist;

    public TrackActionsSheet()
    {
        InitializeComponent();
        ActionsList.ItemsSource = _actions;
    }

    /// <summary>
    /// Opens the sheet for <paramref name="track"/>. Pass <paramref name="playlistId"/> when
    /// opened from within that playlist's own page, to surface "Remove from playlist".
    /// </summary>
    public async Task ShowAsync(TrackDto track, uint? playlistId = null)
    {
        _track = track;
        _playlistId = playlistId;

        TrackTitleLabel.Text = track.Title;
        TrackArtistLabel.Text = track.AllArtists;
        BuildMainActions();

        IsVisible = true;
        Sheet.TranslationY = Sheet.Height > 0 ? Sheet.Height : 600;
        await Task.WhenAll(
            Sheet.TranslateToAsync(0, 0, 200, Easing.CubicOut),
            Backdrop.FadeToAsync(0.5, 200));
    }

    private async Task HideAsync()
    {
        await Task.WhenAll(
            Sheet.TranslateToAsync(0, Sheet.Height, 180, Easing.CubicIn),
            Backdrop.FadeToAsync(0, 180));
        IsVisible = false;
    }

    private void OnBackdropTapped(object? sender, TappedEventArgs e)
    {
        _ = HideAsync();
    }

    private async void OnActionTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is SheetActionItem item)
            {
                await item.Execute();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Track action failed");
        }
    }

    private void BuildMainActions()
    {
        _actions.Clear();
        _actions.Add(new SheetActionItem("Add to playlist", ShowPlaylistPicker));

        if (_playlistId is { } playlistId)
        {
            _actions.Add(new SheetActionItem("Remove from playlist", () => RemoveFromPlaylist(playlistId)));
        }

        _actions.Add(new SheetActionItem("Add to queue", async () =>
        {
            AddToQueue();
            await HideAsync();
        }));

        _actions.Add(new SheetActionItem("Play next", async () =>
        {
            PlayNext();
            await HideAsync();
        }));

        if (_track is { } track)
        {
            if (!IsHostedOnAlbum(track.AlbumId))
            {
                _actions.Add(new SheetActionItem("Go to album", () => NavigateTo($"albumdetail?id={track.AlbumId}")));
            }

            var otherArtists = track.Artists.Where(a => !IsHostedOnArtist(a.Id)).ToList();
            foreach (var artist in otherArtists)
            {
                // Only name the artist when there's more than one to choose between.
                var label = track.Artists.Count > 1 ? $"Go to {artist.Name}" : "Go to artist";
                _actions.Add(new SheetActionItem(label, () => NavigateTo($"artistdetail?id={artist.Id}")));
            }
        }

        _actions.Add(new SheetActionItem("Track details", ShowTrackDetails));
    }

    /// <summary>
    /// True when this sheet sits on the detail page of that album already, where
    /// "Go to album" would just push a duplicate of the page the user is looking at.
    /// </summary>
    private bool IsHostedOnAlbum(uint albumId)
    {
        return FindHostPage() is AlbumDetailPage { BindingContext: ViewModels.AlbumDetailViewModel vm } && vm.AlbumId == albumId;
    }

    private bool IsHostedOnArtist(uint artistId)
    {
        return FindHostPage() is ArtistDetailPage { BindingContext: ViewModels.ArtistDetailViewModel vm } && vm.ArtistId == artistId;
    }

    private Page? FindHostPage()
    {
        Element? element = Parent;
        while (element is not null and not Page)
        {
            element = element.Parent;
        }

        return element as Page;
    }

    private async Task NavigateTo(string route)
    {
        await HideAsync();
        await Shell.Current.GoToAsync(route);
    }

    private async Task ShowPlaylistPicker()
    {
        _actions.Clear();
        _actions.Add(new SheetActionItem("‹ Back", () =>
        {
            BuildMainActions();
            return Task.CompletedTask;
        }));

        IReadOnlyCollection<PlaylistOnlyDto> playlists;
        try
        {
            var client = ServiceHelper.GetRequiredService<IPlaatClient>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            playlists = await client.GetPlaylists(cts.Token);
        }
        catch
        {
            playlists = [];
        }

        foreach (var playlist in playlists)
        {
            _actions.Add(new SheetActionItem(playlist.PlaylistName, () => AddToPlaylist(playlist)));
        }
    }

    private async Task AddToPlaylist(PlaylistOnlyDto playlist)
    {
        if (_track is not null)
        {
            try
            {
                var client = ServiceHelper.GetRequiredService<IPlaatClient>();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.AddToPlaylist(playlist.Id, _track.Id, cts.Token);
            }
            catch
            {
                // best-effort; a bottom sheet has no good place to surface a failure toast from.
            }
        }

        await HideAsync();
    }

    private async Task RemoveFromPlaylist(uint playlistId)
    {
        if (_track is not null)
        {
            var track = _track;
            try
            {
                var client = ServiceHelper.GetRequiredService<IPlaatClient>();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await client.RemoveFromPlaylist(playlistId, track.Id, cts.Token);
                RemovedFromPlaylist?.Invoke(this, track);
            }
            catch
            {
            }
        }

        await HideAsync();
    }

    private void AddToQueue()
    {
        if (_track is not null)
        {
            ServiceHelper.GetRequiredService<PlayerQueueService>().Add(_track);
        }
    }

    private void PlayNext()
    {
        if (_track is not null)
        {
            ServiceHelper.GetRequiredService<PlayerQueueService>().AddNext(_track);
        }
    }

    private async Task ShowTrackDetails()
    {
        var track = _track;
        await HideAsync();
        if (track is not null)
        {
            await Shell.Current.GoToAsync($"trackdetails?id={track.Id}");
        }
    }
}
