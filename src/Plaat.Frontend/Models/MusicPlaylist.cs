using System.Collections;
using Plaat.Contract;

namespace Plaat.Frontend.Models;

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

public partial class MusicPlaylist(ILogger<MusicPlaylist>? logger = null) : IReadOnlyCollection<TrackDto>
{
    private List<TrackDto> _musicFileList = [];
    private List<TrackDto>? _originalList;

    public RepeatMode RepeatMode { get; set; } = RepeatMode.All;

    /// <summary>
    /// The pre-shuffle track order, or <see langword="null"/> when not shuffled. Exposed so callers
    /// can persist it and restore the shuffled state (including the ability to unshuffle) later.
    /// </summary>
    public IReadOnlyList<TrackDto>? OriginalOrder
    {
        get
        {
            return _originalList;
        }
    }

    public bool IsShuffled
    {
        get
        {
            return _originalList is not null;
        }

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
        }
    }

    private void Shuffle()
    {
        var current = CurrentIndex >= 0 ? _musicFileList[CurrentIndex] : null;
        _originalList = [.. _musicFileList];

        var rng = Random.Shared;
        for (var i = _musicFileList.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (_musicFileList[i], _musicFileList[j]) = (_musicFileList[j], _musicFileList[i]);
        }

        if (current is not null)
        {
            _musicFileList.Remove(current);
            _musicFileList.Insert(0, current);
            CurrentIndex = 0;
        }

        FirePlaylistChanged();
    }

    private void Unshuffle()
    {
        if (_originalList is null)
        {
            return;
        }

        var current = CurrentIndex >= 0 ? _musicFileList[CurrentIndex] : null;
        _musicFileList = _originalList;
        _originalList = null;
        if (current is not null)
        {
            CurrentIndex = _musicFileList.IndexOf(current);
        }

        FirePlaylistChanged();
    }

    private void FirePlaylistChanged()
    {
        var actions = OnPlaylistChanged?.GetInvocationList() ?? [];
        foreach (var action in actions)
        {
            InvokeSafely((Func<Task>)action);
        }
    }

    private void InvokeSafely(Func<Task> action)
    {
        Task task;
        try
        {
            task = action();
        }
        catch (Exception ex)
        {
            if (logger is not null)
            {
                LogSubscriberFailed(logger, ex, nameof(OnPlaylistChanged));
            }

            return;
        }

        _ = task.ContinueWith(
            t =>
            {
                if (logger is not null)
                {
                    LogSubscriberFailed(logger, t.Exception, nameof(OnPlaylistChanged));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public void Add(TrackDto track)
    {
        _musicFileList.Add(track);
        _originalList?.Add(track);

        FirePlaylistChanged();
    }

    public void AddNext(TrackDto track)
    {
        _musicFileList.Insert(CurrentIndex + 1, track);

        if (_originalList is null)
        {
            FirePlaylistChanged();
            return;
        }

        if (CurrentIndex < 0)
        {
            _originalList.Insert(0, track);
            FirePlaylistChanged();
            return;
        }

        var currentTrack = _musicFileList[CurrentIndex];
        var originalIndex = _originalList.IndexOf(currentTrack);
        _originalList.Insert(originalIndex + 1, track);

        FirePlaylistChanged();
    }

    public void RemoveAt(int index)
    {
        if (index < 0 || index >= _musicFileList.Count)
        {
            return;
        }

        var track = _musicFileList[index];
        _musicFileList.RemoveAt(index);
        _originalList?.Remove(track);

        if (index < CurrentIndex)
        {
            CurrentIndex--;
        }
        else if (index == CurrentIndex)
        {
            CurrentIndex = -1;
            IsPlaying = false;
        }

        FirePlaylistChanged();
    }

    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= _musicFileList.Count ||
            toIndex < 0 || toIndex >= _musicFileList.Count ||
            fromIndex == toIndex)
        {
            return;
        }

        var track = _musicFileList[fromIndex];
        _musicFileList.RemoveAt(fromIndex);
        _musicFileList.Insert(toIndex, track);

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

        FirePlaylistChanged();
    }

    public void SetPlayList(IReadOnlyCollection<TrackDto> value, IReadOnlyCollection<TrackDto>? originalOrder = null)
    {
        CurrentIndex = -1;
        _musicFileList = [.. value];
        _originalList = originalOrder?.ToList();

        FirePlaylistChanged();
    }

    public void Play(int index)
    {
        CurrentIndex = index;
        IsPlaying = true;

        FirePlaylistChanged();
    }

    public void Clear()
    {
        _musicFileList = [];
        _originalList = null;
        CurrentIndex = -1;
        IsPlaying = false;

        FirePlaylistChanged();
    }

    public IEnumerator<TrackDto> GetEnumerator()
    {
        return _musicFileList.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public int Count
    {
        get
        {
            return _musicFileList.Count;
        }
    }

    public TrackDto this[int index]
    {
        get
        {
            return _musicFileList[index];
        }
    }

    public bool IsPlaying { get; set; }

    public int CurrentIndex { get; set; } = -1;

    public ulong CurrentValueId
    {
        get
        {
            return CurrentIndex == -1 ? 0ul : _musicFileList[CurrentIndex].Id;
        }
    }

    public TrackDto CurrentValue
    {
        get
        {
            return _musicFileList[CurrentIndex];
        }
    }

    public void Next()
    {
        CurrentIndex = (CurrentIndex + 1) % Count;
        FirePlaylistChanged();
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
            FirePlaylistChanged();

            return TrackEndedAction.Stopped;
        }

        Next();
        IsPlaying = true;

        return TrackEndedAction.Advanced;
    }

    public void Previous()
    {
        CurrentIndex = (CurrentIndex - 1 + Count) % Count;
        FirePlaylistChanged();
    }

    public event Func<Task>? OnPlaylistChanged;

    [LoggerMessage(LogLevel.Error, "Unhandled exception in {Event} subscriber")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception? ex, string @event);
}
