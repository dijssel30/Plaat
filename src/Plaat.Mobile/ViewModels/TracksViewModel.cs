using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;
using Plaat.Mobile.Services.Player;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// The full "browse every track" surface, server-paged: it loads one <see cref="PageSize"/>-row
/// page at a time as the list scrolls (and re-queries server-side while typing), so it never
/// materializes the whole library on the device the way the old Home list did.
/// </summary>
public partial class TracksViewModel(IPlaatClient plaatClient, PlayerQueueService queue) : ViewModelBase
{
    private const int PageSize = 100;

    /// <summary>
    /// A "play from here" queue is capped so tapping a track never pulls the whole library.
    /// </summary>
    private const int MaxQueueTracks = 1000;

    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private CancellationTokenSource? _searchDebounceCts;
    private int _loaded;
    private bool _endReached;

    public BulkObservableCollection<TrackDto> Tracks { get; } = [];

    [ObservableProperty]
    public partial int TotalCount { get; set; }

    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                _ = ResetAndReload();
            }
        }
    } = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await ResetAndReload();
        HasLoadedOnce = true;
    }

    [RelayCommand]
    private async Task LoadMoreAsync()
    {
        if (_endReached)
        {
            return;
        }

        await LoadPage(CancellationToken.None);
    }

    private async Task ResetAndReload()
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        try
        {
            await Task.Delay(SearchDebounce, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _loaded = 0;
        _endReached = false;
        Tracks.ReplaceAll([]);

        await LoadPage(token);
    }

    private async Task LoadPage(CancellationToken token)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            var page = await plaatClient.GetTracksPage(
                new TrackQuery(Search, TrackSortField.Title, false, _loaded, PageSize), token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            foreach (var track in page.Items)
            {
                Tracks.Add(track);
            }

            _loaded += page.Items.Count;
            TotalCount = page.TotalCount;
            _endReached = page.Items.Count == 0 || _loaded >= page.TotalCount;
            ErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer query
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load tracks: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async void PlayTrack(TrackDto track)
    {
        try
        {
            var ids = (await plaatClient.GetTrackIds(
                new TrackQuery(Search, TrackSortField.Title, false, 0, 0), CancellationToken.None)).ToList();

            var start = ids.IndexOf(track.Id);
            if (start < 0)
            {
                start = 0;
            }

            var slice = ids.Skip(start).Take(MaxQueueTracks).ToArray();
            var dtos = await plaatClient.GetTracksByIds(slice, CancellationToken.None);

            if (dtos.Count == 0)
            {
                return;
            }

            queue.SetPlayList(dtos);
            queue.Play(0);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't start playback: {ex.Message}";
        }
    }
}
