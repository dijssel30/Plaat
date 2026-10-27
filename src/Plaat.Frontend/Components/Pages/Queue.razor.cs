using Microsoft.AspNetCore.Components;
using Plaat.Contract;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Queue : ComponentBase, IDisposable
{
    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    private IReadOnlyCollection<TrackDto> _musicFileList = [];

    protected override void OnInitialized()
    {
        _musicFileList = MusicPlayList.ToList();

        MusicPlayList.OnPlaylistChanged += MusicPlayListUpdate;
    }

    private async Task MusicPlayListUpdate()
    {
        _musicFileList = MusicPlayList.ToList();
        await InvokeAsync(StateHasChanged);
    }

    public void Dispose()
    {
        MusicPlayList.OnPlaylistChanged -= MusicPlayListUpdate;
        GC.SuppressFinalize(this);
    }
}
