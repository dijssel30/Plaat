using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web.Virtualization;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Tracks : ComponentBase, IDisposable
{
    /// <summary>
    /// A "play from here" queue is capped so tapping a track in a 100k library never
    /// materializes the whole library into the circuit.
    /// </summary>
    private const int MaxQueueTracks = 1000;

    private const int SearchDebounceMs = 250;

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    private Virtualize<TrackDto>? _virtualize;
    private int? _totalCount;
    private bool _queueCapped;

    private TrackSortField _sort = TrackSortField.Title;
    private bool _descending;

    private CancellationTokenSource? _debounceCts;
    private ulong _currentPlayingId;

    private string? _search;

    private string? Search
    {
        get
        {
            return _search;
        }

        set
        {
            if (_search == value)
            {
                return;
            }

            _search = value;
            _ = DebouncedRefresh();
        }
    }

    private TrackQuery CurrentQuery(int skip = 0, int take = 0)
    {
        return new(_search, _sort, _descending, skip, take);
    }

    protected override void OnInitialized()
    {
        MusicPlayList.OnPlaylistChanged += OnPlaylistChanged;
        _currentPlayingId = MusicPlayList.CurrentValueId;
    }

    private async Task OnPlaylistChanged()
    {
        if (_currentPlayingId == MusicPlayList.CurrentValueId)
        {
            return;
        }

        _currentPlayingId = MusicPlayList.CurrentValueId;
        await InvokeAsync(StateHasChanged);
    }

    private async ValueTask<ItemsProviderResult<TrackDto>> LoadTracks(ItemsProviderRequest request)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(Timeouts.NormalTimeout);

        var page = await PlaatClient.GetTracksPage(
            CurrentQuery(request.StartIndex, request.Count), cts.Token);

        if (_totalCount != page.TotalCount)
        {
            _totalCount = page.TotalCount;
            await InvokeAsync(StateHasChanged);
        }

        return new ItemsProviderResult<TrackDto>(page.Items, page.TotalCount);
    }

    private async Task DebouncedRefresh()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (_virtualize is not null)
        {
            await _virtualize.RefreshDataAsync();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task SetSort(TrackSortField field)
    {
        if (_sort == field)
        {
            _descending = !_descending;
        }
        else
        {
            _sort = field;
            _descending = false;
        }

        if (_virtualize is not null)
        {
            await _virtualize.RefreshDataAsync();
        }
    }

    private string Indicator(TrackSortField field)
    {
        return _sort != field ? string.Empty : _descending ? "▼" : "▲";
    }

    private async Task PlayFrom(TrackDto track)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(Timeouts.NormalTimeout);

        var ids = (await PlaatClient.GetTrackIds(CurrentQuery(), cts.Token)).ToList();

        var startIndex = ids.IndexOf(track.Id);
        if (startIndex < 0)
        {
            startIndex = 0;
        }

        var slice = ids.Skip(startIndex).Take(MaxQueueTracks).ToArray();
        _queueCapped = ids.Count - startIndex > MaxQueueTracks;

        var dtos = await PlaatClient.GetTracksByIds(slice, cts.Token);
        if (dtos.Count == 0)
        {
            return;
        }

        MusicPlayList.IsShuffled = false;
        MusicPlayList.SetPlayList(dtos);
        MusicPlayList.Play(0);
    }

    public void Dispose()
    {
        MusicPlayList.OnPlaylistChanged -= OnPlaylistChanged;
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
