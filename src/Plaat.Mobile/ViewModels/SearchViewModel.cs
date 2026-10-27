using CommunityToolkit.Mvvm.Input;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Search calls the server-side <c>/api/search</c> endpoint (debounced), which filters the API's
/// in-memory catalog with the exact same normalization the app used to run locally - so results are
/// identical, but a keystroke transfers a capped result set instead of the whole library.
///
/// Results are exposed as one flat, heterogeneous list (<see cref="Results"/>) - section header
/// rows interleaved with artist/album/track rows - so the page can render them in a single
/// virtualized <c>CollectionView</c> instead of three non-virtualized ones stacked in a ScrollView.
/// </summary>
public partial class SearchViewModel(IPlaatClient plaatClient, PlayerQueueService queue, LoopbackProxyService proxy) : ViewModelBase
{
    private const int MinQueryLength = 2;
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _debounceCts;

    // The track subset of the current results, in display order - what PlayTrack queues.
    private List<TrackDto> _trackResults = [];

    public BulkObservableCollection<object> Results { get; } = [];

    public LoopbackProxyService Proxy
    {
        get
        {
            return proxy;
        }
    }

    public bool HasQuery
    {
        get
        {
            return (Search?.Trim().Length ?? 0) >= MinQueryLength;
        }
    }

    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(HasQuery));
                _ = RunSearch(value);
            }
        }
    } = string.Empty;

    // Pages call LoadCommand on appear; nothing to preload any more.
    [RelayCommand]
    private Task LoadAsync()
    {
        HasLoadedOnce = true;
        return Task.CompletedTask;
    }

    public void PlayTrack(TrackDto track)
    {
        var index = _trackResults.IndexOf(track);
        if (index < 0)
        {
            return;
        }

        queue.SetPlayList(_trackResults);
        queue.Play(index);
    }

    private async Task RunSearch(string? query)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < MinQueryLength)
        {
            _trackResults = [];
            Results.ReplaceAll([]);
            return;
        }

        try
        {
            await Task.Delay(Debounce, token);

            var results = await plaatClient.Search(query.Trim(), token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _trackResults = [.. results.Tracks];
            Results.ReplaceAll(BuildRows(results));
            ErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            // superseded by a newer keystroke
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Search failed: {ex.Message}";
        }
    }

    private static IEnumerable<object> BuildRows(SearchResultsDto results)
    {
        if (results.Artists.Count > 0)
        {
            yield return new SearchHeader("Artists");
            foreach (var artist in results.Artists)
            {
                yield return artist;
            }
        }

        if (results.Albums.Count > 0)
        {
            yield return new SearchHeader("Albums");
            foreach (var album in results.Albums)
            {
                yield return album;
            }
        }

        if (results.Tracks.Count > 0)
        {
            yield return new SearchHeader("Tracks");
            foreach (var track in results.Tracks)
            {
                yield return track;
            }
        }
    }
}
