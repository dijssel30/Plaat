using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages.AdminPanels;

public partial class MonitoredFoldersPanel : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required ILogger<MonitoredFoldersPanel> Logger { get; init; }

    private IReadOnlyCollection<MonitoredFolderDto> _monitoredFolders = [];
    private string _newFolderPath = "";
    private string? _folderMessage;
    private bool _folderError;
    private bool _isAddingFolder;
    private bool _isTriggeringScan;
    private uint? _busyFolderId;

    protected override async Task OnInitializedAsync()
    {
        await LoadMonitoredFolders();
    }

    private async Task LoadMonitoredFolders()
    {
        _monitoredFolders = await PlaatClient.GetMonitoredFolders(CancellationToken.None);
    }

    private async Task AddFolder()
    {
        _folderMessage = null;

        if (string.IsNullOrWhiteSpace(_newFolderPath))
        {
            _folderMessage = "Folder path is required.";
            _folderError = true;
            return;
        }

        _isAddingFolder = true;

        try
        {
            var added = await PlaatClient.AddMonitoredFolder(_newFolderPath, CancellationToken.None);

            if (added)
            {
                _folderMessage = $"Folder '{_newFolderPath}' added.";
                _folderError = false;
                _newFolderPath = "";

                await LoadMonitoredFolders();
            }
            else
            {
                _folderMessage = "That folder is already monitored.";
                _folderError = true;
            }
        }
        catch (Exception ex)
        {
            LogAddFolderFailed(Logger, ex, _newFolderPath);
            _folderMessage = "Failed to add folder.";
            _folderError = true;
        }
        finally
        {
            _isAddingFolder = false;
        }
    }

    private async Task RemoveFolder(uint id)
    {
        _busyFolderId = id;

        try
        {
            await PlaatClient.RemoveMonitoredFolder(id, CancellationToken.None);

            await LoadMonitoredFolders();
        }
        catch (Exception ex)
        {
            LogRemoveFolderFailed(Logger, ex, id);
            _folderMessage = "Failed to remove folder.";
            _folderError = true;
        }
        finally
        {
            _busyFolderId = null;
        }
    }

    private async Task ForceRescan()
    {
        _folderMessage = null;
        _isTriggeringScan = true;

        try
        {
            var started = await PlaatClient.TriggerFolderScan(CancellationToken.None);

            _folderMessage = started
                ? "Rescan started."
                : "A scan is already in progress.";
            _folderError = false;
        }
        catch (Exception ex)
        {
            LogRescanFailed(Logger, ex);
            _folderMessage = "Failed to start rescan.";
            _folderError = true;
        }
        finally
        {
            _isTriggeringScan = false;
        }
    }

    [LoggerMessage(LogLevel.Error, "Failed to add monitored folder {FolderPath}")]
    private static partial void LogAddFolderFailed(ILogger logger, Exception ex, string folderPath);

    [LoggerMessage(LogLevel.Error, "Failed to remove monitored folder {FolderId}")]
    private static partial void LogRemoveFolderFailed(ILogger logger, Exception ex, uint folderId);

    [LoggerMessage(LogLevel.Error, "Failed to start monitored folder rescan")]
    private static partial void LogRescanFailed(ILogger logger, Exception ex);
}
