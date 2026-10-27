using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages.AdminPanels;

public partial class TasksPanel : ComponentBase, IAsyncDisposable
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    private TasksStatusDto _status = new(false, null, false, [], false, 0, 0);
    private string? _message;
    private bool _error;
    private bool _isTriggeringScan;
    private bool _isTriggeringConvert;
    private bool _isTriggeringLoudnessAnalysis;

    private readonly PeriodicTimer _pollTimer = new(TimeSpan.FromSeconds(3));
    private Task? _pollLoop;

    protected override async Task OnInitializedAsync()
    {
        await RefreshStatus();

        _pollLoop = PollStatusAsync();
    }

    private async Task PollStatusAsync()
    {
        while (await _pollTimer.WaitForNextTickAsync())
        {
            await RefreshStatus();
            await InvokeAsync(StateHasChanged);
        }
    }

    private async Task RefreshStatus()
    {
        try
        {
            _status = await PlaatClient.GetTasksStatus(CancellationToken.None);
        }
        catch
        {
            // keep showing the last known status; the next poll will retry
        }
    }

    private async Task StartScan()
    {
        _message = null;
        _isTriggeringScan = true;

        try
        {
            var started = await PlaatClient.TriggerFolderScan(CancellationToken.None);

            _message = started ? "Scan started." : "A scan is already in progress.";
            _error = false;

            await RefreshStatus();
        }
        catch (Exception ex)
        {
            _message = $"Failed to start scan: {ex.Message}";
            _error = true;
        }
        finally
        {
            _isTriggeringScan = false;
        }
    }

    private async Task StartConvert()
    {
        _message = null;
        _isTriggeringConvert = true;

        try
        {
            var started = await PlaatClient.TriggerConvert(CancellationToken.None);

            _message = started ? "Convert run started." : "A convert run is already in progress.";
            _error = false;

            await RefreshStatus();
        }
        catch (Exception ex)
        {
            _message = $"Failed to start convert run: {ex.Message}";
            _error = true;
        }
        finally
        {
            _isTriggeringConvert = false;
        }
    }

    private async Task StartLoudnessAnalysis()
    {
        _message = null;
        _isTriggeringLoudnessAnalysis = true;

        try
        {
            var started = await PlaatClient.TriggerLoudnessAnalysis(CancellationToken.None);

            _message = started ? "Loudness analysis started." : "Loudness analysis is already in progress.";
            _error = false;

            await RefreshStatus();
        }
        catch (Exception ex)
        {
            _message = $"Failed to start loudness analysis: {ex.Message}";
            _error = true;
        }
        finally
        {
            _isTriggeringLoudnessAnalysis = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _pollTimer.Dispose();

        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop;
            }
            catch
            {
                // loop observes timer disposal via WaitForNextTickAsync returning false; nothing to surface
            }
        }
    }
}
