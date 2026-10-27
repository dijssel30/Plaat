using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Plaat.Contract;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Player;

public partial class QueuePanel : ComponentBase, IDisposable
{
    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    [Inject]
    public required IJSRuntime JsRuntime { get; init; }

    [Inject]
    public required NavPlaylistsState NavPlaylistsState { get; init; }

    // Owned by the parent PlayerBar (rather than locally) so it can enforce mutual exclusivity
    // with the fullscreen cover art view on mobile, where both would otherwise cover the screen.
    [Parameter]
    public bool IsOpen { get; set; }

    [Parameter]
    public EventCallback<bool> IsOpenChanged { get; set; }

    private bool _showHistory;
    private ElementReference _wrapperRef;
    private DotNetObjectReference<QueuePanel>? _dotNetRef;
    private bool _outsideClickListenerActive;

    private int? _dragIndex;
    private TrackDto? _draggedTrack;
    private int? _dragOverIndex;

    protected override void OnInitialized()
    {
        MusicPlayList.OnPlaylistChanged += Update;
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (IsOpen && !_outsideClickListenerActive)
        {
            _outsideClickListenerActive = true;
            await JsRuntime.InvokeVoidAsync("uiHelpers.addOutsideClickListener", _dotNetRef, _wrapperRef);
        }
        else if (!IsOpen && _outsideClickListenerActive)
        {
            _outsideClickListenerActive = false;
            await JsRuntime.InvokeVoidAsync("uiHelpers.removeOutsideClickListener", _wrapperRef);
        }
    }

    [JSInvokable]
    public async Task CloseFromOutsideClick()
    {
        if (!IsOpen)
        {
            return;
        }

        await IsOpenChanged.InvokeAsync(false);
    }

    private async Task Update()
    {
        await InvokeAsync(StateHasChanged);
    }

    private async Task ToggleOpen()
    {
        await IsOpenChanged.InvokeAsync(!IsOpen);
    }

    private void SetTab(bool showHistory)
    {
        _showHistory = showHistory;
    }

    /// <summary>
    /// Position-based, not playback-history-based: "Previous" is everything before
    /// CurrentIndex in queue order (closest to now-playing first), regardless of whether
    /// it was actually played yet.
    /// </summary>
    private List<int> Entries
    {
        get
        {
            var current = MusicPlayList.CurrentIndex;

            if (_showHistory)
            {
                if (current <= 0)
                {
                    return [];
                }

                return [.. Enumerable.Range(0, current).Reverse()];
            }

            var start = Math.Max(current, 0);
            return [.. Enumerable.Range(start, Math.Max(MusicPlayList.Count - start, 0))];
        }
    }

    private void Play(int index)
    {
        MusicPlayList.Play(index);
    }

    /// <summary>
    /// Only the upcoming tracks (after the one now playing) can be dragged or dropped onto, so a
    /// reorder never moves the current track or pushes a track into "Previous".
    /// </summary>
    private bool CanReorder(int index)
    {
        return !_showHistory && index > MusicPlayList.CurrentIndex;
    }

    private void OnDragStart(int index)
    {
        if (!CanReorder(index))
        {
            return;
        }

        _dragIndex = index;
        _draggedTrack = MusicPlayList[index];
        NavPlaylistsState.BeginDrag(_draggedTrack);
    }

    private void OnDragOver(int index)
    {
        if (_dragIndex is null || _dragIndex == index || !CanReorder(index))
        {
            return;
        }

        _dragOverIndex = index;
    }

    private void OnDrop(int index)
    {
        var fromIndex = _dragIndex;
        var draggedTrack = _draggedTrack;
        ClearDragState();

        // The queue can change mid-drag (the current track finishing, another tab editing it), so
        // only move when the dragged index still holds the track that was picked up.
        if (fromIndex is null || fromIndex == index || !CanReorder(index) ||
            fromIndex >= MusicPlayList.Count || !ReferenceEquals(MusicPlayList[fromIndex.Value], draggedTrack))
        {
            return;
        }

        MusicPlayList.Move(fromIndex.Value, index);
    }

    private void OnDragEnd()
    {
        ClearDragState();
        NavPlaylistsState.EndDrag();
    }

    private void ClearDragState()
    {
        _dragIndex = null;
        _draggedTrack = null;
        _dragOverIndex = null;
    }

    private string RowClass(int index)
    {
        if (_dragIndex == index)
        {
            return "dragging";
        }

        if (_dragIndex is null || _dragOverIndex != index)
        {
            return string.Empty;
        }

        return _dragIndex < index ? "drop-after" : "drop-before";
    }

    public void Dispose()
    {
        MusicPlayList.OnPlaylistChanged -= Update;
        _dotNetRef?.Dispose();
        GC.SuppressFinalize(this);
    }
}
