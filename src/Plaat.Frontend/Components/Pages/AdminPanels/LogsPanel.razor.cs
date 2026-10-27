using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages.AdminPanels;

public partial class LogsPanel : ComponentBase, IDisposable
{
    private const int SearchDebounceMs = 250;

    // The database sink only persists Warning and above (see DatabaseLogger.IsEnabled).
    private static readonly string[] AvailableLevels = ["Warning", "Error", "Critical"];

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private readonly HashSet<string> _selectedLevels = [];

    private IReadOnlyCollection<LogEntryDto> _entries = [];
    private string? _message;
    private bool _isLoading;
    private string? _search;
    private CancellationTokenSource? _debounceCts;

    private bool HasFilters
    {
        get
        {
            return _selectedLevels.Count > 0 || !string.IsNullOrWhiteSpace(_search);
        }
    }

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

    protected override async Task OnInitializedAsync()
    {
        await Refresh();
    }

    private async Task ToggleLevel(string level)
    {
        // Nothing selected means "every level", which keeps the filter row usable without
        // forcing the admin to tick all three boxes back on.
        if (!_selectedLevels.Remove(level))
        {
            _selectedLevels.Add(level);
        }

        await Refresh();
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

        await Refresh();
        await InvokeAsync(StateHasChanged);
    }

    private async Task Refresh()
    {
        _message = null;
        _isLoading = true;

        try
        {
            var query = new LogQuery(
                _selectedLevels.Count is 0 ? null : [.. _selectedLevels],
                string.IsNullOrWhiteSpace(_search) ? null : _search);

            _entries = await PlaatClient.GetLogs(query, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _message = $"Failed to load logs: {ex.Message}";
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
    }
}
