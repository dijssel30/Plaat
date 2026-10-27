using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Playlists;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Playlist : ComponentBase, IDisposable
{
    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    [Inject]
    public required NavPlaylistsState NavPlaylistsState { get; init; }

    [Inject]
    public required NavigationManager NavigationManager { get; init; }

    [Parameter]
    public required string PlaylistName { get; init; }

    private PlaylistDetailsDto? _playlist;

    private List<TrackDto> _musicFileList = [];

    private bool _editMode;

    private bool _loading = true;

    private bool _showDuplicatePlaylistModal;
    private string _duplicateName = "";
    private string? _duplicateModalError;
    private bool _duplicateIsSaving;

    private bool _showEditSmartPlaylistModal;
    private SmartRuleGroupUiModel _editSmartRootGroup = new();
    private bool _editLimitEnabled;
    private int _editLimitValue = 25;
    private string? _editModalError;
    private bool _editIsSaving;

    private readonly CancellationTokenSource _cts = new();

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;

        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        _playlist = await PlaatClient.GetPlaylist(PlaylistName, tokenSource.Token);

        _musicFileList = _playlist?.Tracks.ToList() ?? [];
        _editMode = false;
        _loading = false;

        CloseDuplicatePlaylistModal();
    }

    private void ToggleEditMode()
    {
        _editMode = !_editMode;
    }

    private void OpenDuplicatePlaylistModal()
    {
        if (_playlist is null)
        {
            return;
        }

        _duplicateName = $"{_playlist.PlaylistName} (copy)";
        _duplicateModalError = null;
        _duplicateIsSaving = false;
        _showDuplicatePlaylistModal = true;
    }

    private void CloseDuplicatePlaylistModal()
    {
        _showDuplicatePlaylistModal = false;
        _duplicateModalError = null;
        _duplicateIsSaving = false;
    }

    private async Task OnDuplicateNameKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await ConfirmDuplicatePlaylist();
        }
    }

    private async Task ConfirmDuplicatePlaylist()
    {
        if (_playlist is null)
        {
            return;
        }

        var newName = _duplicateName.Trim();

        if (newName.Length == 0)
        {
            _duplicateModalError = "Playlist name cannot be empty.";
            return;
        }

        _duplicateIsSaving = true;
        _duplicateModalError = null;

        try
        {
            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            tokenSource.CancelAfter(Timeouts.NormalTimeout);

            var duplicated = await PlaatClient.DuplicatePlaylist(
                _playlist.Id, _playlist.IsSmart, newName, tokenSource.Token);

            if (!duplicated)
            {
                _duplicateModalError = "A playlist with this name already exists.";
                _duplicateIsSaving = false;
                return;
            }

            await NavPlaylistsState.RefreshAsync(tokenSource.Token);

            CloseDuplicatePlaylistModal();

            NavigationManager.NavigateTo($"/playlists/{Uri.EscapeDataString(newName)}");
        }
        catch (Exception)
        {
            _duplicateModalError = "Failed to duplicate playlist.";
            _duplicateIsSaving = false;
        }
    }

    private async Task OpenEditSmartPlaylistModal()
    {
        if (_playlist is not { IsSmart: true })
        {
            return;
        }

        _editModalError = null;

        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        var rules = await PlaatClient.GetSmartPlaylistRules(_playlist.Id, tokenSource.Token);

        if (rules is null)
        {
            return;
        }

        _editSmartRootGroup = SmartRuleMapper.FromDto(rules.RootGroup);
        _editLimitEnabled = rules.Limit is not null;
        _editLimitValue = rules.Limit ?? 25;
        _showEditSmartPlaylistModal = true;
    }

    private void CloseEditSmartPlaylistModal()
    {
        _showEditSmartPlaylistModal = false;
        _editModalError = null;
        _editIsSaving = false;
    }

    private void OnEditLimitValueChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val) && val > 0)
        {
            _editLimitValue = val;
        }
    }

    private async Task ConfirmEditSmartPlaylist()
    {
        if (_playlist is not { IsSmart: true })
        {
            return;
        }

        if (!SmartRuleMapper.HasAnyRule(_editSmartRootGroup))
        {
            _editModalError = "Add at least one rule.";
            return;
        }

        if (SmartRuleMapper.HasInvalidDurationRangeRule(_editSmartRootGroup))
        {
            _editModalError = "Duration range rules need at least a minimum or maximum.";
            return;
        }

        _editIsSaving = true;
        _editModalError = null;

        try
        {
            var request = new CreateSmartPlaylistRequest
            {
                RootGroup = SmartRuleMapper.ToDto(_editSmartRootGroup),
                Limit = _editLimitEnabled ? _editLimitValue : null,
                LimitOrderBy = _editLimitEnabled ? SmartPlaylistLimitOrderByDto.RecentlyAdded : null
            };

            using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            tokenSource.CancelAfter(Timeouts.NormalTimeout);

            var updated = await PlaatClient.UpdateSmartPlaylist(_playlist.Id, request, tokenSource.Token);

            if (!updated)
            {
                _editModalError = "Failed to update playlist.";
                _editIsSaving = false;
                return;
            }

            _playlist = await PlaatClient.GetPlaylist(PlaylistName, tokenSource.Token);
            _musicFileList = _playlist?.Tracks.ToList() ?? [];

            CloseEditSmartPlaylistModal();
        }
        catch (Exception)
        {
            _editModalError = "Failed to update playlist.";
            _editIsSaving = false;
        }
    }

    private async Task OnRemoveTrack(int index)
    {
        if (_playlist is not { IsSmart: false } || index < 0 || index >= _musicFileList.Count)
        {
            return;
        }

        var track = _musicFileList[index];

        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        await PlaatClient.RemoveFromPlaylist(_playlist.Id, track.Id, tokenSource.Token);

        _musicFileList.RemoveAt(index);
    }

    private async Task OnMoveTrack((int From, int To) move)
    {
        if (_playlist is not { IsSmart: false } ||
            move.From < 0 || move.From >= _musicFileList.Count ||
            move.To < 0 || move.To >= _musicFileList.Count ||
            move.From == move.To)
        {
            return;
        }

        var track = _musicFileList[move.From];
        _musicFileList.RemoveAt(move.From);
        _musicFileList.Insert(move.To, track);

        using var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        await PlaatClient.ReorderPlaylist(_playlist.Id, _musicFileList.Select(t => t.Id).ToList(), tokenSource.Token);
    }

    private void PlayAll()
    {
        if (_musicFileList.Count == 0)
        {
            return;
        }

        MusicPlayList.IsShuffled = false;
        MusicPlayList.SetPlayList(_musicFileList);
        MusicPlayList.Play(0);
    }

    private void ShuffleAll()
    {
        if (_musicFileList.Count == 0)
        {
            return;
        }

        MusicPlayList.IsShuffled = false;
        MusicPlayList.SetPlayList(_musicFileList);
        MusicPlayList.IsShuffled = true;
        MusicPlayList.Play(0);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
