using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Playlists;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Playlists : ComponentBase, IDisposable
{
    [Inject]
    public required IPlaatClient PlaatClient { get; set; }

    [Inject]
    public required NavPlaylistsState NavPlaylistsState { get; set; }

    private string? _search;

    private bool _showCreatePlaylistModal;
    private bool _showCreateSmartPlaylistModal;
    private string _newPlaylistName = "";
    private string? _modalError;
    private bool _isSaving;
    private SmartRuleGroupUiModel _smartRootGroup = new();
    private bool _limitEnabled;
    private int _limitValue = 25;
    private PlaylistOnlyDto? _playlistToDelete;

    private List<PlaylistOnlyDto> FilteredPlaylists
    {
        get
        {
            return string.IsNullOrWhiteSpace(_search)
            ? [.. NavPlaylistsState.Playlists.OrderBy(l => l.PlaylistName, StringComparer.OrdinalIgnoreCase)]
            : [.. NavPlaylistsState.Playlists
                .Where(x => x.PlaylistName.Contains(_search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(l => l.PlaylistName, StringComparer.OrdinalIgnoreCase)];
        }
    }

    private static string GetHref(string name)
    {
        return $"Playlists/{Uri.EscapeDataString(name)}";
    }

    protected override async Task OnInitializedAsync()
    {
        NavPlaylistsState.OnPlaylistsChanged += HandlePlaylistsChanged;

        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        await NavPlaylistsState.EnsureLoadedAsync(tokenSource.Token);
    }

    private async Task HandlePlaylistsChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        NavPlaylistsState.OnPlaylistsChanged -= HandlePlaylistsChanged;
        GC.SuppressFinalize(this);
    }

    private void OpenCreatePlaylistModal()
    {
        _newPlaylistName = "";
        _modalError = null;
        _showCreatePlaylistModal = true;
    }

    private void OpenCreateSmartPlaylistModal()
    {
        _newPlaylistName = "";
        _modalError = null;
        _smartRootGroup = new SmartRuleGroupUiModel { Rules = [new SmartRuleUiModel()] };
        _limitEnabled = false;
        _limitValue = 25;
        _showCreateSmartPlaylistModal = true;
    }

    private void CloseModals()
    {
        _showCreatePlaylistModal = false;
        _showCreateSmartPlaylistModal = false;
        _playlistToDelete = null;
        _modalError = null;
        _isSaving = false;
    }

    private void OpenDeleteModal(PlaylistOnlyDto playlist)
    {
        _modalError = null;
        _playlistToDelete = playlist;
    }

    private async Task ConfirmDeletePlaylist()
    {
        if (_playlistToDelete is null)
        {
            return;
        }

        _isSaving = true;
        _modalError = null;

        try
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(Timeouts.NormalTimeout);
            await PlaatClient.DeletePlaylist(_playlistToDelete.Id, _playlistToDelete.IsSmart, cts.Token);

            await RefreshPlaylists();
            CloseModals();
        }
        catch (Exception)
        {
            _modalError = "Failed to delete playlist.";
            _isSaving = false;
        }
    }

    private async Task OnPlaylistNameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await ConfirmCreatePlaylist();
        }
    }

    private async Task ConfirmCreatePlaylist()
    {
        if (string.IsNullOrWhiteSpace(_newPlaylistName))
        {
            _modalError = "Playlist name cannot be empty.";
            return;
        }

        _isSaving = true;
        _modalError = null;

        try
        {
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(Timeouts.NormalTimeout);
            var created = await PlaatClient.CreatePlaylist(_newPlaylistName.Trim(), cts.Token);

            if (!created)
            {
                _modalError = "A playlist with this name already exists.";
                _isSaving = false;
                return;
            }

            await RefreshPlaylists();
            CloseModals();
        }
        catch (Exception)
        {
            _modalError = "Failed to create playlist.";
            _isSaving = false;
        }
    }

    private async Task ConfirmCreateSmartPlaylist()
    {
        if (string.IsNullOrWhiteSpace(_newPlaylistName))
        {
            _modalError = "Playlist name cannot be empty.";
            return;
        }

        if (!SmartRuleMapper.HasAnyRule(_smartRootGroup))
        {
            _modalError = "Add at least one rule.";
            return;
        }

        if (SmartRuleMapper.HasInvalidDurationRangeRule(_smartRootGroup))
        {
            _modalError = "Duration range rules need at least a minimum or maximum.";
            return;
        }

        _isSaving = true;
        _modalError = null;

        try
        {
            var request = new CreateSmartPlaylistRequest
            {
                RootGroup = SmartRuleMapper.ToDto(_smartRootGroup),
                Limit = _limitEnabled ? _limitValue : null,
                LimitOrderBy = _limitEnabled ? SmartPlaylistLimitOrderByDto.RecentlyAdded : null
            };

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(Timeouts.NormalTimeout);
            var created = await PlaatClient.CreateSmartPlaylist(_newPlaylistName.Trim(), request, cts.Token);

            if (!created)
            {
                _modalError = "A playlist with this name already exists.";
                _isSaving = false;
                return;
            }

            await RefreshPlaylists();
            CloseModals();
        }
        catch (Exception)
        {
            _modalError = "Failed to create playlist.";
            _isSaving = false;
        }
    }

    private async Task RefreshPlaylists()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(Timeouts.NormalTimeout);
        await NavPlaylistsState.RefreshAsync(cts.Token);
    }

    private async Task TogglePin(PlaylistOnlyDto playlist)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(Timeouts.NormalTimeout);
        await PlaatClient.SetPlaylistPinned(playlist.Id, playlist.IsSmart, !playlist.IsPinned, cts.Token);
        await RefreshPlaylists();
    }

    private void OnLimitValueChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val) && val > 0)
        {
            _limitValue = val;
        }
    }
}
