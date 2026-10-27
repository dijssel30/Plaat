using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// One artist of the currently playing track. The now-playing screen renders these side by side
/// instead of binding TrackDto.AllArtists to a single label, so each name is its own tap target -
/// with one label, tapping anywhere on "Armin van Buuren, Bon Jovi" could only ever open one of
/// them. <see cref="Text"/> carries the trailing separator so the row still reads as "A, B".
/// </summary>
public sealed record NowPlayingArtist(uint Id, string Text);

/// <summary>
/// One star of the current track's rating row - filled or not. Modelled as a bound collection of
/// image sources rather than five separately bound Image elements so the XAML stays a single
/// templated layout. The five instances live for the life of the row and only swap their
/// <see cref="Source"/>, so painting the row mid-swipe doesn't tear down and rebuild the images
/// under the finger.
/// </summary>
public sealed partial class NowPlayingStar(string source) : ObservableObject
{
    [ObservableProperty]
    public partial string Source { get; set; } = source;
}

/// <summary>
/// Backs the fullscreen "now playing" overlay - the native equivalent of PlayerBar.razor.cs's
/// fullscreen toggle. Reads/drives the app-wide PlayerQueueService and PlaybackEngine singletons;
/// position/duration are polled on a short UI-refresh timer separate from
/// PlaybackStateSyncService's 500ms backend-persistence loop (this one is purely for the seek bar).
/// </summary>
public partial class NowPlayingViewModel : ViewModelBase, IDisposable
{
    private const string FilledStar = "star_filled.svg";
    private const string EmptyStar = "star_empty.svg";

    // Without an explicit budget these calls inherit HttpClient's 100s default (see
    // PlaybackStateSyncService's same-shaped budgets) - fatal for a star tap, which needs to
    // either land or roll back within about as long as a user will wait for feedback.
    private static readonly TimeSpan RatingRequestTimeout = TimeSpan.FromSeconds(5);

    private readonly PlayerQueueService _queue;
    private readonly PlaybackEngine _playbackEngine;
    private readonly LoopbackProxyService _proxy;
    private readonly IPlaatClient _plaatClient;
    private readonly IDispatcherTimer _timer;

    [ObservableProperty]
    public partial string TrackTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial IReadOnlyList<NowPlayingArtist> Artists { get; set; } = [];

    [ObservableProperty]
    public partial string AlbumName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? CoverArtUrl { get; set; }

    [ObservableProperty]
    public partial uint? AlbumId { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial bool IsShuffled { get; set; }

    [ObservableProperty]
    public partial RepeatMode RepeatMode { get; set; }

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 1;

    /// <summary>
    /// The current track's trim start (0 when unset) - bound as the seek bar's <c>Minimum</c> so its
    /// fill/thumb math is scoped to the trim window, while <see cref="PositionSeconds"/>/
    /// <see cref="DurationSeconds"/> stay absolute for the time labels.
    /// </summary>
    [ObservableProperty]
    public partial double TrimStartSeconds { get; set; }

    /// <summary>
    /// The current track's star rating, as five filled/empty star images.
    /// </summary>
    public IReadOnlyList<NowPlayingStar> Stars { get; } =
        [.. Enumerable.Range(0, TrackRatingDto.MaxStars).Select(_ => new NowPlayingStar(EmptyStar))];

    /// <summary>
    /// The rating actually stored, as opposed to what the row paints mid-swipe.
    /// </summary>
    public int Rating { get; private set; }

    private bool _isSeeking;

    // Whether this view-model is currently wired to the queue's events. Subscription is tied to the
    // page being on screen (Resume/Dispose, called from OnAppearing/OnDisappearing) rather than to
    // construction: NowPlayingPage is transient, so a subscription taken in the constructor and never
    // released outlived every close - each fullscreen-player open left another dead view-model wired
    // to the singleton queue, running Refresh() on every position tick forever. That steadily growing
    // pile of work (and the GC churn behind it) is what made playback hitch on opening the player.
    private bool _subscribed;

    // Track the artist row was last built for - see UpdateArtists.
    private uint? _artistsTrackId;

    // Track the star row was last loaded for - see UpdateRating.
    private uint? _ratingTrackId;

    // Guards SetRating against overlapping saves: without it, a slow first tap and an impatient
    // second one race, and whichever request's response (success, failure or rollback) lands last
    // wins - even if that means stomping a newer rating back to a stale value. Mirrors PlayerBar's
    // _savingRating on the web frontend.
    private bool _savingRating;

    // The last position this view-model itself pushed into PositionSeconds, so SliderValueChanged
    // can tell our own writes - echoed straight back by the Slider's binding - apart from a real
    // user tap on the track. A plain "am I writing right now?" flag isn't enough: applying the
    // bindings replays the current PositionSeconds into a freshly built Slider long after the write
    // (NowPlayingPage's constructor, on `BindingContext = _viewModel`), and that echo used to seek
    // the player to where it already was on every fullscreen open - which still costs a real
    // ExoPlayer seek, throwing away the audio it had already buffered ahead and re-decoding from
    // the last polled position, heard as playback jumping a fraction of a second backwards.
    private double _lastPushedPosition = double.NaN;

    // Set while UpdatePosition writes the seek bar's range and value. Slider clamps Value to
    // [Minimum, Maximum] the moment either side is set, and raises ValueChanged with the *clamped*
    // number - which _lastPushedPosition can't recognise as ours. That bites whenever the track
    // changed while this page wasn't refreshing (covered by QueuePage, or the app backgrounded with
    // it on top): the bar still held the previous track's range, so pushing a position past the old
    // track's length clamped to it and seeked the new track back there; shrinking Maximum under a
    // stale Value could likewise seek to the end and skip the track.
    private bool _updatingPosition;

    // After a seek, ignore the timer's position reads briefly: MediaElement.Position keeps returning
    // the pre-seek value for a beat, which would otherwise snap the thumb straight back.
    private DateTime _ignorePositionUntil = DateTime.MinValue;

    public NowPlayingViewModel(
        PlayerQueueService queue,
        PlaybackEngine playbackEngine,
        LoopbackProxyService proxy,
        IPlaatClient plaatClient)
    {
        _queue = queue;
        _playbackEngine = playbackEngine;
        _proxy = proxy;
        _plaatClient = plaatClient;

        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(500);
        _timer.Tick += (_, _) => UpdatePosition();

        Subscribe();
        _timer.Start();

        Refresh();
    }

    private void Subscribe()
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _queue.PlaylistChanged += Refresh;
        _queue.PropertyChanged += OnQueuePropertyChanged;
    }

    private void Unsubscribe()
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
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

        TrackTitle = current?.Title ?? string.Empty;
        AlbumName = current?.Album ?? string.Empty;
        CoverArtUrl = current is null ? null : _proxy.TrackCoverUrl(current.Id).ToString();
        AlbumId = current?.AlbumId;
        UpdateArtists(current);
        UpdateRating(current);
        IsPlaying = _queue.IsPlaying;
        IsShuffled = _queue.IsShuffled;
        RepeatMode = _queue.RepeatMode;

        UpdatePosition();
    }

    /// <summary>
    /// Rebuilds the artist row only when the track actually changes: Refresh runs on every queue
    /// property change (play/pause included), and handing the bound layout a new collection each
    /// time would make it tear down and re-create the labels for nothing.
    /// </summary>
    private void UpdateArtists(TrackDto? current)
    {
        if (current?.Id == _artistsTrackId)
        {
            return;
        }

        _artistsTrackId = current?.Id;

        var artists = current?.Artists.ToList() ?? [];
        Artists =
        [
            .. artists.Select((artist, index) =>
                new NowPlayingArtist(artist.Id, index < artists.Count - 1 ? $"{artist.Name}," : artist.Name))
        ];
    }

    /// <summary>
    /// Refreshes the star row when the track changes. Ratings are per user and are not carried on
    /// <see cref="TrackDto"/> (the API's track lists are cached process-wide, shared by every user),
    /// so they are fetched per track - which is why this is guarded the same way
    /// <see cref="UpdateArtists"/> is: Refresh runs on every queue property change, and re-fetching
    /// on each play/pause would be pure waste.
    /// </summary>
    private void UpdateRating(TrackDto? current)
    {
        if (current?.Id == _ratingTrackId)
        {
            return;
        }

        _ratingTrackId = current?.Id;
        Rating = TrackRatingDto.MinStars;
        PaintStars(Rating);

        if (current is not null)
        {
            _ = LoadRating(current.Id);
        }
    }

    private async Task LoadRating(uint trackId)
    {
        try
        {
            using var timeout = new CancellationTokenSource(RatingRequestTimeout);
            var stars = await _plaatClient.GetTrackRating(trackId, timeout.Token);

            // The queue may have moved on while the fetch was in flight - don't show one track's
            // rating against another.
            if (_ratingTrackId == trackId)
            {
                Rating = stars;
                PaintStars(stars);
            }
        }
        catch (Exception ex)
        {
            // A missing rating is not worth an error banner over the player - leave the row empty.
            System.Diagnostics.Debug.WriteLine($"Failed to load rating for track {trackId}: {ex}");
        }
    }

    /// <summary>
    /// Paints the row at the given rating without saving anything. NowPlayingPage calls this on
    /// every step of a swipe so the stars follow the finger; the value is only persisted on release.
    /// </summary>
    public void PaintStars(int stars)
    {
        for (var i = 0; i < Stars.Count; i++)
        {
            Stars[i].Source = i < stars ? FilledStar : EmptyStar;
        }
    }

    /// <summary>
    /// Persists a rating for the track playing now. The row is painted straight away and rolled
    /// back if the save fails. Ignored while a previous save is still in flight - see
    /// <see cref="_savingRating"/> - so a second impatient tap can't race the first.
    /// </summary>
    public async Task SetRating(int stars)
    {
        if (_ratingTrackId is not { } trackId || _savingRating)
        {
            return;
        }

        var previous = Rating;

        Rating = stars;
        PaintStars(stars);
        _savingRating = true;

        try
        {
            using var timeout = new CancellationTokenSource(RatingRequestTimeout);

            if (!await _plaatClient.SetTrackRating(trackId, stars, timeout.Token))
            {
                RollBackRating(trackId, previous);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save rating for track {trackId}: {ex}");
            RollBackRating(trackId, previous);
        }
        finally
        {
            _savingRating = false;
        }
    }

    /// <summary>
    /// Puts a failed rating back, unless the queue has moved on to another track meanwhile - in
    /// which case the row already belongs to that one and must be left alone.
    /// </summary>
    private void RollBackRating(uint trackId, int previous)
    {
        if (_ratingTrackId != trackId)
        {
            return;
        }

        Rating = previous;
        PaintStars(previous);
    }

    private void UpdatePosition()
    {
        if (_isSeeking || DateTime.UtcNow < _ignorePositionUntil)
        {
            return;
        }

        var current = _queue.CurrentValue;

        _updatingPosition = true;
        try
        {
            // Range before value, so the position lands inside the range it belongs to - see
            // _updatingPosition for what the reverse order did.
            TrimStartSeconds = current?.TrimStart?.TotalSeconds ?? 0;
            DurationSeconds = Math.Max(current?.TrimEnd?.TotalSeconds ?? _playbackEngine.Duration.TotalSeconds, 1);
            PushPosition(_playbackEngine.Position.TotalSeconds);
        }
        finally
        {
            _updatingPosition = false;
        }
    }

    /// <summary>
    /// Writes a position we produced ourselves - see <see cref="_lastPushedPosition"/>.
    /// </summary>
    private void PushPosition(double seconds)
    {
        _lastPushedPosition = seconds;
        PositionSeconds = seconds;
    }

    [RelayCommand]
    private void TogglePlayPause()
    {
        if (_queue.CurrentIndex < 0)
        {
            return;
        }

        _queue.IsPlaying = !_queue.IsPlaying;
    }

    [RelayCommand]
    private void Next()
    {
        _queue.Next();
    }

    [RelayCommand]
    private void Previous()
    {
        _queue.Previous();
    }

    [RelayCommand]
    private void ToggleShuffle()
    {
        _queue.IsShuffled = !_queue.IsShuffled;
    }

    [RelayCommand]
    private void ToggleRepeat()
    {
        _queue.RepeatMode = _queue.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off
        };
        RepeatMode = _queue.RepeatMode;
    }

    public void BeginSeek()
    {
        _isSeeking = true;
    }

    public Task EndSeek(double seconds)
    {
        _isSeeking = false;
        return SeekTo(seconds);
    }

    /// <summary>
    /// Handles a tap on a spot of the seek bar. Android's <see cref="Slider"/> doesn't raise
    /// DragStarted/DragCompleted for a plain tap - only ValueChanged - so without this a tap just
    /// moved the thumb (via the two-way binding) and the next timer tick snapped it back, with the
    /// audio never actually seeking.
    /// </summary>
    public void SliderValueChanged(double seconds)
    {
        if (_isSeeking || _updatingPosition || seconds.Equals(_lastPushedPosition))
        {
            return;
        }

        _ = SeekTo(seconds);
    }

    private async Task SeekTo(double seconds)
    {
        var clamped = Math.Clamp(seconds, TrimStartSeconds, Math.Max(TrimStartSeconds, DurationSeconds));
        PushPosition(clamped);
        _ignorePositionUntil = DateTime.UtcNow.AddSeconds(1);
        await _playbackEngine.SeekTo(TimeSpan.FromSeconds(clamped), CancellationToken.None);
    }

    /// <summary>
    /// Re-wires the queue subscription and restarts the position timer after <see cref="Dispose"/>
    /// tore them down - needed because NowPlayingPage.OnDisappearing disposes on every navigation
    /// *away* from the page, including just being covered by QueuePage (which reuses this same
    /// page/view-model instance rather than replacing it - see QueuePage.xaml's comment). Without
    /// resuming here on OnAppearing, the seek bar would go dead - and the play/pause and track
    /// display would stop tracking the queue - the first time the queue is opened and closed.
    /// </summary>
    public void Resume()
    {
        Subscribe();
        Refresh();
        _timer.Start();
    }

    public void Dispose()
    {
        _timer.Stop();
        Unsubscribe();
        GC.SuppressFinalize(this);
    }
}
