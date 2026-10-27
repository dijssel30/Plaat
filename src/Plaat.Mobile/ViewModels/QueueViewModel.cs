using CommunityToolkit.Mvvm.ComponentModel;
using Plaat.Contract;
using Plaat.Mobile.Collections;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Wraps a queued TrackDto with an IsCurrent flag the DataTemplate can bind to directly
/// (the template's x:DataType is this class, not TrackDto, precisely so that binding works).
/// </summary>
public partial class QueueTrackItem(TrackDto track, bool isCurrent) : ObservableObject
{
    public TrackDto Track { get; } = track;

    [ObservableProperty]
    public partial bool IsCurrent { get; set; } = isCurrent;
}

/// <summary>
/// Direct port of Plaat.Frontend/Components/Pages/Queue.razor.cs - reads PlayerQueueService only, no API calls of its own.
/// </summary>
public partial class QueueViewModel : ViewModelBase
{
    private readonly PlayerQueueService _queue;

    // Whether this view-model is currently wired to the queue - see Subscribe.
    private bool _subscribed;

    public BulkObservableCollection<QueueTrackItem> Tracks { get; } = [];

    public LoopbackProxyService Proxy { get; }

    public QueueViewModel(PlayerQueueService queue, LoopbackProxyService proxy)
    {
        _queue = queue;
        Proxy = proxy;
        Subscribe();
    }

    /// <summary>
    /// Index of the currently playing track, for the page to scroll to on open. -1 if none.
    /// </summary>
    public int CurrentIndex
    {
        get
        {
            return _queue.CurrentIndex;
        }
    }

    /// <summary>
    /// Wires this view-model to the queue and catches up on anything missed meanwhile. QueuePage
    /// pairs it with <see cref="Unsubscribe"/> from OnAppearing/OnDisappearing: the page is transient,
    /// and a subscription taken once in the constructor and never released kept every Queue page ever
    /// opened alive - each one rebuilding its own full copy of the queue on every track change, on the
    /// UI thread, for the rest of the session. Same fix as NowPlayingViewModel's.
    /// </summary>
    public void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _queue.PlaylistChanged += Refresh;
        Refresh();
    }

    public void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        _queue.PlaylistChanged -= Refresh;
    }

    private void Refresh()
    {
        var currentIndex = _queue.CurrentIndex;

        // One Reset notification instead of a Clear plus one Add notification per queued track.
        Tracks.ReplaceAll(_queue.Select((track, index) => new QueueTrackItem(track, index == currentIndex)));
    }

    public void PlayTrack(QueueTrackItem item)
    {
        var index = Tracks.IndexOf(item);
        if (index >= 0)
        {
            _queue.Play(index);
        }
    }

    public void RemoveTrack(QueueTrackItem item)
    {
        var index = Tracks.IndexOf(item);
        if (index >= 0)
        {
            _queue.RemoveAt(index);
        }
    }

    /// <summary>
    /// Commits a drag-to-reorder to the real queue. The page already live-reorders its own
    /// <see cref="Tracks"/> copy for visual feedback while dragging (see QueuePage.xaml.cs) without
    /// touching the underlying queue, so this applies the same move to the source of truth using the
    /// original (pre-drag) index - once it does, PlaylistChanged fires and Refresh() rebuilds Tracks
    /// from the queue's new order, which already matches what was shown live.
    /// </summary>
    public void CommitDrag(int fromIndex, int toIndex)
    {
        if (fromIndex != toIndex)
        {
            _queue.Move(fromIndex, toIndex);
        }
    }
}
