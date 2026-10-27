using Microsoft.AspNetCore.Components;
using Plaat.Contract.Models;
using Plaat.Frontend.Auth;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Layout;

public partial class NavMenu : IDisposable
{
    [Inject]
    public required AuthService AuthService { get; set; }

    [Inject]
    public required NavPlaylistsState NavPlaylistsState { get; set; }

    private bool _dragActive;
    private (uint Id, bool IsSmart)? _dragOverPlaylist;

    private async Task Logout()
    {
        await AuthService.Logout();
    }

    protected override async Task OnInitializedAsync()
    {
        NavPlaylistsState.OnPlaylistsChanged += HandlePlaylistsChanged;
        NavPlaylistsState.OnDragStateChanged += HandleDragStateChanged;

        await NavPlaylistsState.EnsureLoadedAsync();
    }

    private IEnumerable<PlaylistOnlyDto> OrderedPlaylists
    {
        get
        {
            return NavPlaylistsState.Playlists
            .Where(p => p.IsPinned)
            .OrderBy(p => p.PlaylistName, StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsValidDropTarget(PlaylistOnlyDto playlist)
    {
        return !playlist.IsSmart;
    }

    private static (uint Id, bool IsSmart) KeyOf(PlaylistOnlyDto playlist)
    {
        return (playlist.Id, playlist.IsSmart);
    }

    private void OnPlaylistDragOver(PlaylistOnlyDto playlist)
    {
        if (NavPlaylistsState.DraggedTrack is null || !IsValidDropTarget(playlist))
        {
            return;
        }

        _dragOverPlaylist = KeyOf(playlist);
    }

    private void OnPlaylistDragLeave(PlaylistOnlyDto playlist)
    {
        if (_dragOverPlaylist == KeyOf(playlist))
        {
            _dragOverPlaylist = null;
        }
    }

    private async Task OnPlaylistDrop(PlaylistOnlyDto playlist)
    {
        _dragOverPlaylist = null;

        await NavPlaylistsState.DropOnPlaylistAsync(playlist);
    }

    private string PlaylistItemClass(PlaylistOnlyDto playlist)
    {
        var classes = "nav-item px-3 playlist-nav-item";

        if (_dragActive && IsValidDropTarget(playlist))
        {
            classes += " drop-armed";
        }

        if (_dragOverPlaylist == KeyOf(playlist))
        {
            classes += " drop-target";
        }

        return classes;
    }

    private async Task HandlePlaylistsChanged()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async Task HandleDragStateChanged()
    {
        _dragActive = NavPlaylistsState.DraggedTrack is not null;

        if (!_dragActive)
        {
            _dragOverPlaylist = null;
        }

        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        NavPlaylistsState.OnPlaylistsChanged -= HandlePlaylistsChanged;
        NavPlaylistsState.OnDragStateChanged -= HandleDragStateChanged;
        GC.SuppressFinalize(this);
    }
}
