using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels.Player;

/// <summary>
/// Pure display state for <see cref="Views.Player.MiniPlayerBar"/> - deliberately has no MediaElement
/// of its own. It's a thin, bound "remote control" over the app-wide <see cref="PlayerQueueService"/>;
/// the single real MediaElement lives in HomePage (see that file's comment for why).
/// </summary>
public partial class MiniPlayerBarViewModel : ObservableObject
{
    private readonly PlayerQueueService _queue;
    private readonly LoopbackProxyService _proxy;

    [ObservableProperty]
    public partial bool HasQueue { get; set; }

    [ObservableProperty]
    public partial string CurrentTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentArtist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CoverArtUrl { get; set; }

    [ObservableProperty]
    public partial string PlayPauseIcon { get; set; } = "play.svg";

    public MiniPlayerBarViewModel(PlayerQueueService queue, LoopbackProxyService proxy)
    {
        _queue = queue;
        _proxy = proxy;
        Refresh();
    }

    /// <summary>
    /// Starts listening for queue changes and catches up on anything missed while unsubscribed.
    /// The hosting <see cref="Views.Player.MiniPlayerBar"/> calls this from its Loaded handler
    /// rather than just the constructor - a Shell tab's page (and this bar within it) is kept alive
    /// and reused across tab switches, so this needs to be safely callable more than once per
    /// instance.
    /// </summary>
    public void Subscribe()
    {
        _queue.PlaylistChanged += Refresh;
        // PlaylistChanged only fires on queue mutations (add/remove/next/...); IsPlaying can also
        // be toggled directly (e.g. from NowPlayingPage) without going through those, so also
        // watch property-level changes to keep the play/pause icon in sync either way.
        _queue.PropertyChanged += OnQueuePropertyChanged;
        Refresh();
    }

    public void Unsubscribe()
    {
        _queue.PlaylistChanged -= Refresh;
        _queue.PropertyChanged -= OnQueuePropertyChanged;
    }

    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Raised right after CurrentIndex for the very same change, which already refreshed everything.
        if (e.PropertyName is nameof(PlayerQueueService.CurrentValue) or nameof(PlayerQueueService.CurrentValueId) or nameof(PlayerQueueService.HasQueue))
        {
            return;
        }

        Refresh();
    }

    private void Refresh()
    {
        var current = _queue.CurrentValue;

        HasQueue = current is not null;
        CurrentTitle = current?.Title ?? string.Empty;
        CurrentArtist = current?.AllArtists ?? string.Empty;
        CoverArtUrl = current is null ? null : _proxy.TrackCoverUrl(current.Id).ToString();
        PlayPauseIcon = _queue.IsPlaying ? "pause.svg" : "play.svg";
    }

    public void TogglePlayPause()
    {
        if (_queue.CurrentIndex < 0)
        {
            return;
        }

        _queue.IsPlaying = !_queue.IsPlaying;
    }

    public void Next()
    {
        _queue.Next();
    }
}
