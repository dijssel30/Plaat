using Microsoft.AspNetCore.Components;
using MudBlazor;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components;

public partial class MusicFileList : IDisposable
{
    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required NavPlaylistsState NavPlaylistsState { get; init; }

    [Inject]
    public required ISnackbar Snackbar { get; init; }

    [Parameter]
    [EditorRequired]
    public required IReadOnlyCollection<TrackDto> Music { get; set; }

    /// <summary>
    /// The full set of tracks a click should queue up, when the rows on screen are deliberately
    /// only part of it - a collapsed "popular tracks" list, or one disc of a multi-disc album.
    /// Null (the default) means "queue exactly what's displayed".
    /// </summary>
    [Parameter]
    public IReadOnlyCollection<TrackDto>? QueueMusic { get; set; }

    [Parameter]
    public bool AskConfirmation { get; set; } = true;

    [Parameter]
    public bool ShowSearchBar { get; set; } = true;

    [Parameter]
    public bool ShowQueueControls { get; set; }

    [Parameter]
    public bool ShowEditControls { get; set; }

    [Parameter]
    public EventCallback<int> OnRemoveTrack { get; set; }

    [Parameter]
    public EventCallback<(int From, int To)> OnMoveTrack { get; set; }

    private bool ShowControls
    {
        get
        {
            return ShowQueueControls || ShowEditControls;
        }
    }

    private IReadOnlyCollection<PlaylistOnlyDto> _allPlaylists = [];

    private List<(int Index, TrackDto Item)> _indexedMusic = [];

    private List<(int Index, TrackDto Item)> SortedIndexedMusic
    {
        get
        {
            return ApplySort(_indexedMusic);
        }
    }

    private List<(int Index, TrackDto Item)> ApplySort(List<(int Index, TrackDto Item)> source)
    {
        var sorted = _sortColumn switch
        {
            SortColumn.Title => source.OrderBy(x => x.Item.Title, StringComparer.OrdinalIgnoreCase),
            SortColumn.Artist => source.OrderBy(x => x.Item.AllArtists, StringComparer.OrdinalIgnoreCase),
            SortColumn.Album => source.OrderBy(x => x.Item.Album, StringComparer.OrdinalIgnoreCase),
            SortColumn.Year => source.OrderBy(x => x.Item.Year),
            SortColumn.Plays => source.OrderBy(x => x.Item.PlayCount),
            _ => source.AsEnumerable()
        };

        if (_sortColumn != SortColumn.None && !_sortAscending)
        {
            sorted = sorted.Reverse();
        }

        return [.. sorted];
    }

    /// <summary>
    /// The tracks a click sends to the player: everything in <see cref="QueueMusic"/> when the
    /// caller supplied it (so hidden tracks still get queued), otherwise just the displayed rows.
    /// Either way it follows whatever column sort is active, so the queue order matches what the
    /// user is looking at.
    /// </summary>
    private List<TrackDto> QueueTracks
    {
        get
        {
            return QueueMusic is null
            ? [.. SortedIndexedMusic.Select(x => x.Item)]
            : [.. ApplySort([.. QueueMusic.Index()]).Select(x => x.Item)];
        }
    }

    private List<(int Index, TrackDto Item)> FilteredMusic
    {
        get
        {
            var sorted = SortedIndexedMusic;

            if (string.IsNullOrWhiteSpace(_search))
            {
                return sorted;
            }

            return [.. sorted
                .Select(x => (Entry: x, Score: SearchScoring.SearchScore(_search, x.Item.Title, x.Item.AllArtists)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Entry)];
        }
    }

    private enum SortColumn
    {
        None,
        Title,
        Artist,
        Album,
        Year,
        Plays
    }

    private SortColumn _sortColumn = SortColumn.None;
    private bool _sortAscending = true;

    private bool _showPlaylistChoice;
    private int _pendingIndex;
    private string? _search;
    private uint? _trackInfoId;

    private int? _dragIndex;
    private int? _dragOverIndex;

    private void SetSort(SortColumn column)
    {
        if (ShowControls)
        {
            // Reordering relies on drag/drop indices matching the underlying list order,
            // so column sorting is disabled while it's possible to avoid moving the wrong track.
            return;
        }

        if (_sortColumn == column)
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortColumn = column;
            _sortAscending = true;
        }
    }

    private string SortIndicator(SortColumn column)
    {
        if (_sortColumn != column)
        {
            return string.Empty;
        }

        return _sortAscending ? "▲" : "▼";
    }

    protected override async Task OnInitializedAsync()
    {
        MusicPlayList.OnPlaylistChanged += OnMusicPlayListChanged;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        _allPlaylists = await PlaatClient.GetPlaylists(cts.Token);
    }

    private async Task OnMusicPlayListChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        MusicPlayList.OnPlaylistChanged -= OnMusicPlayListChanged;
        GC.SuppressFinalize(this);
    }

    protected override void OnParametersSet()
    {
        _indexedMusic = [.. Music.Index()];

        if (ShowControls)
        {
            _sortColumn = SortColumn.None;
            _sortAscending = true;
        }
    }

    private ulong CurrentPlayingId
    {
        get
        {
            return MusicPlayList.CurrentValueId;
        }
    }

    private void SelectTrack(int index)
    {
        if (Music.ElementAt(index).Id == CurrentPlayingId)
        {
            return; // don´t do anything if track already playing
        }

        if (CurrentPlayingId > 0 && AskConfirmation)
        {
            _pendingIndex = index;
            _showPlaylistChoice = true;
            return;
        }

        PlayOverwrite(index);
    }

    private void OverwriteQueue()
    {
        PlayOverwrite(_pendingIndex);
        CloseModal();
    }

    private void AddToQueue()
    {
        MusicPlayList.Add(Music.ElementAt(_pendingIndex));
        CloseModal();
    }

    private void AddToQueue(TrackDto track)
    {
        MusicPlayList.Add(track);
    }

    private void PlayNext(TrackDto track)
    {
        MusicPlayList.AddNext(track);
    }

    private void PlayOverwrite(int index)
    {
        var orderedTracks = QueueTracks;
        var trackId = Music.ElementAt(index).Id;
        var newIndex = orderedTracks.FindIndex(t => t.Id == trackId);

        if (newIndex < 0)
        {
            return;
        }

        MusicPlayList.SetPlayList(orderedTracks);
        MusicPlayList.Play(newIndex);
    }

    private void CloseModal()
    {
        _showPlaylistChoice = false;
    }

    private void ShowTrackInfo(uint trackId)
    {
        _trackInfoId = trackId;
    }

    private void CloseTrackInfo()
    {
        _trackInfoId = null;
    }

    private async Task RemoveTrack(int index)
    {
        if (ShowQueueControls)
        {
            MusicPlayList.RemoveAt(index);
        }
        else if (ShowEditControls)
        {
            await OnRemoveTrack.InvokeAsync(index);
        }
    }

    private void OnDragStart(int index, TrackDto item)
    {
        if (ShowControls)
        {
            _dragIndex = index;
        }

        NavPlaylistsState.BeginDrag(item);
    }

    private void OnDragOver(int index)
    {
        if (_dragIndex is null || _dragIndex == index)
        {
            return;
        }

        _dragOverIndex = index;
    }

    private async Task OnDrop(int index)
    {
        if (_dragIndex is not null && _dragIndex != index)
        {
            if (ShowQueueControls)
            {
                MusicPlayList.Move(_dragIndex.Value, index);
            }
            else if (ShowEditControls)
            {
                await OnMoveTrack.InvokeAsync((_dragIndex.Value, index));
            }
        }

        _dragIndex = null;
        _dragOverIndex = null;
    }

    private void OnDragEnd()
    {
        _dragIndex = null;
        _dragOverIndex = null;
        NavPlaylistsState.EndDrag();
    }

    private string RowClass(int index)
    {
        if (_dragIndex is null || _dragOverIndex != index || _dragIndex == index)
        {
            return _dragIndex == index ? "dragging" : string.Empty;
        }

        return _dragIndex < index ? "drop-after" : "drop-before";
    }

    private async Task ReloadMetadata(uint trackId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            var success = await PlaatClient.ReloadTrackMetadata(trackId, cts.Token);

            Snackbar.Add(
                success ? "Metadata reloaded from file" : "Track file could not be found on disk",
                success ? Severity.Success : Severity.Warning);
        }
        catch
        {
            Snackbar.Add("Failed to reload metadata", Severity.Error);
        }
    }

    private async Task AddToPlaylist(ulong playlistId, ulong trackId)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        try
        {
            await PlaatClient.AddToPlaylist(playlistId, trackId, cts.Token);
        }
        catch
        {
            // log
        }
    }
}
