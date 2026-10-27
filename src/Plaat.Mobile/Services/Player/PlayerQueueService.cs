using System.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using Plaat.Contract;

namespace Plaat.Mobile.Services.Player;

public enum RepeatMode
{
    Off,
    All,
    One
}

public enum TrackEndedAction
{
    Stopped,
    Advanced,
    RepeatTrack
}

/// <summary>
/// The app-wide "current queue" singleton: track list, current index, shuffle/repeat state.
/// A near-direct port of Plaat.Frontend/Models/MusicPlaylist.cs - see that file for the original
/// Blazor version this mirrors. Kept free of any MediaElement/playback-engine dependency, same
/// separation of concerns as the original (the actual player lives in PlaybackEngine).
/// </summary>
public partial class PlayerQueueService : ObservableObject, IReadOnlyCollection<TrackDto>
{
    private List<TrackDto> _tracks = [];
    private List<TrackDto>? _originalOrder;
    private int _currentIndex = -1;

    [ObservableProperty]
    public partial RepeatMode RepeatMode { get; set; } = RepeatMode.All;

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    public int CurrentIndex
    {
        get { return _currentIndex; }
        set
        {
            if (SetProperty(ref _currentIndex, value))
            {
                OnPropertyChanged(nameof(CurrentValue));
                OnPropertyChanged(nameof(CurrentValueId));
                OnPropertyChanged(nameof(HasQueue));
            }
        }
    }

    /// <summary>
    /// The pre-shuffle track order, or null when not shuffled.
    /// </summary>
    public IReadOnlyList<TrackDto>? OriginalOrder
    {
        get { return _originalOrder; }
    }

    public bool IsShuffled
    {
        get { return _originalOrder is not null; }
        set
        {
            if (value == IsShuffled)
            {
                return;
            }

            if (value)
            {
                Shuffle();
            }
            else
            {
                Unshuffle();
            }

            OnPropertyChanged();
        }
    }

    public bool HasQueue
    {
        get { return _tracks.Count > 0; }
    }

    public int Count
    {
        get { return _tracks.Count; }
    }

    public TrackDto? CurrentValue
    {
        get { return _currentIndex >= 0 && _currentIndex < _tracks.Count ? _tracks[_currentIndex] : null; }
    }

    public ulong CurrentValueId
    {
        get { return CurrentValue?.Id ?? 0ul; }
    }

    /// <summary>
    /// Fired after any mutation, for consumers that just want to know "something changed, refresh
    /// my derived list" (e.g. the Queue page) rather than binding to individual properties.
    /// </summary>
    public event Action? PlaylistChanged;

    private void Shuffle()
    {
        var current = CurrentValue;
        _originalOrder = [.. _tracks];

        var rng = Random.Shared;
        for (var i = _tracks.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_tracks[i], _tracks[j]) = (_tracks[j], _tracks[i]);
        }

        if (current is not null)
        {
            _tracks.Remove(current);
            _tracks.Insert(0, current);
            CurrentIndex = 0;
        }

        Raise();
    }

    private void Unshuffle()
    {
        if (_originalOrder is null)
        {
            return;
        }

        var current = CurrentValue;
        _tracks = _originalOrder;
        _originalOrder = null;

        if (current is not null)
        {
            CurrentIndex = _tracks.IndexOf(current);
        }

        Raise();
    }

    public void Add(TrackDto track)
    {
        _tracks.Add(track);
        _originalOrder?.Add(track);
        Raise();
    }

    public void AddNext(TrackDto track)
    {
        _tracks.Insert(CurrentIndex + 1, track);

        if (_originalOrder is not null)
        {
            if (CurrentIndex < 0)
            {
                _originalOrder.Insert(0, track);
            }
            else
            {
                var currentTrack = _tracks[CurrentIndex];
                var originalIndex = _originalOrder.IndexOf(currentTrack);
                _originalOrder.Insert(originalIndex + 1, track);
            }
        }

        Raise();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _tracks.Count)
        {
            return;
        }

        var track = _tracks[index];
        _tracks.RemoveAt(index);
        _originalOrder?.Remove(track);

        if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (index == CurrentIndex)
        {
            CurrentIndex = -1;
            IsPlaying = false;
        }

        Raise();
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _tracks.Count ||
            toIndex < 0 || toIndex >= _tracks.Count ||
            fromIndex == toIndex)
        {
            return;
        }

        var track = _tracks[fromIndex];
        _tracks.RemoveAt(fromIndex);
        _tracks.Insert(toIndex, track);

        if (CurrentIndex == fromIndex)
        {
            CurrentIndex = toIndex;
        }
        else if (fromIndex < CurrentIndex && toIndex >= CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (fromIndex > CurrentIndex && toIndex <= CurrentIndex)
        {
            CurrentIndex++;
        }

        Raise();
    }

    public void SetPlayList(IReadOnlyCollection<TrackDto> value, IReadOnlyCollection<TrackDto>? originalOrder = null)
    {
        CurrentIndex = -1;
        _tracks = [.. value];
        _originalOrder = originalOrder?.ToList();
        OnPropertyChanged(nameof(IsShuffled));

        Raise();
    }

    public void Play(int index)
    {
        CurrentIndex = index;
        IsPlaying = true;
        Raise();
    }

    public void Clear()
    {
        _tracks = [];
        _originalOrder = null;
        CurrentIndex = -1;
        IsPlaying = false;
        Raise();
    }

    public void Next()
    {
        if (Count == 0)
        {
            return;
        }

        CurrentIndex = (CurrentIndex + 1) % Count;
        Raise();
    }

    public void Previous()
    {
        if (Count == 0)
        {
            return;
        }

        CurrentIndex = (CurrentIndex - 1 + Count) % Count;
        Raise();
    }

    /// <summary>
    /// Decides what should happen when the current track finishes playing, honoring <see cref="RepeatMode"/>.
    /// </summary>
    public TrackEndedAction HandleTrackEnded()
    {
        if (Count == 0)
        {
            return TrackEndedAction.Stopped;
        }

        if (RepeatMode == RepeatMode.One)
        {
            return TrackEndedAction.RepeatTrack;
        }

        if (RepeatMode == RepeatMode.Off && CurrentIndex >= Count - 1)
        {
            IsPlaying = false;
            Raise();
            return TrackEndedAction.Stopped;
        }

        Next();
        IsPlaying = true;

        return TrackEndedAction.Advanced;
    }

    private void Raise()
    {
        PlaylistChanged?.Invoke();
    }

    public IEnumerator<TrackDto> GetEnumerator()
    {
        return _tracks.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
