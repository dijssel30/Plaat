using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Search : ComponentBase, IDisposable
{
    private const int MinQueryLength = 2;
    private const int SearchDebounceMs = 250;

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private SearchResultsDto _results = new();
    private bool _searching;
    private string? _search;

    private CancellationTokenSource? _debounceCts;

    private bool HasQuery
    {
        get
        {
            return (_search?.Trim().Length ?? 0) >= MinQueryLength;
        }
    }

    private bool IsEmpty
    {
        get
        {
            return _results.Artists.Count == 0 && _results.Albums.Count == 0 && _results.Tracks.Count == 0;
        }
    }

    private string? SearchText
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
            _ = DebouncedSearch();
        }
    }

    private async Task DebouncedSearch()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;

        if (!HasQuery)
        {
            _results = new SearchResultsDto();
            _searching = false;
            StateHasChanged();
            return;
        }

        try
        {
            await Task.Delay(SearchDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        _searching = true;
        StateHasChanged();

        try
        {
            var results = await PlaatClient.Search(_search!.Trim(), token);

            if (token.IsCancellationRequested)
            {
                return;
            }

            _results = results;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            _results = new SearchResultsDto();
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                _searching = false;
                StateHasChanged();
            }
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
