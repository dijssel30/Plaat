using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using MudBlazor;
using Plaat.Contract;
using Plaat.Contract.Client;
using Plaat.Contract.Models;
using Plaat.Frontend.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Album : ComponentBase, IDisposable
{
    // Largest cover image the browser is allowed to stream to the circuit; the API enforces
    // the same ceiling server-side.
    private const long MaxCoverImageBytes = 15 * 1024 * 1024;

    [Inject]
    public required IPlaatClient PlaatClient { get; init; }

    [Inject]
    public required ISnackbar Snackbar { get; init; }

    [Inject]
    public required MusicPlaylist MusicPlayList { get; init; }

    [Parameter]
    public required int AlbumId { get; init; }

    private AlbumDetailsDto? _album;

    private List<TrackDto> _musicFileList = [];

    // The album's tracks split per disc, in disc order. Only rendered as separate, headed lists
    // when there's more than one disc.
    private List<(uint Number, List<TrackDto> Tracks)> _discs = [];

    private bool _loading = true;

    private bool _isFavorite;

    private bool _uploadingCover;

    // Bumped after a successful cover upload so the <img> URL changes and the browser
    // refetches instead of serving the previous image from its cache.
    private string _coverVersion = string.Empty;

    private MudFileUpload<IBrowserFile>? _coverUpload;

    private readonly CancellationTokenSource _cts = new();

    protected override async Task OnParametersSetAsync()
    {
        _loading = true;

        using var tokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        _album = await PlaatClient.GetAlbum(AlbumId, tokenSource.Token);

        _musicFileList = _album?.Tracks
            .OrderBy(t => t.DiscNumber)
            .ThenBy(t => t.TrackNumber)
            .ToList() ?? [];

        _discs = [.. _musicFileList
            .GroupBy(t => t.DiscNumber)
            .OrderBy(g => g.Key)
            .Select(g => (Number: g.Key, Tracks: g.ToList()))];

        _isFavorite = _album is not null && await PlaatClient.IsAlbumFavorite((uint)AlbumId, tokenSource.Token);

        _loading = false;
    }

    /// <summary>
    /// The album's release year, or a "first-last" range when its tracks disagree (compilations,
    /// or an album whose tracks were tagged with their original recording years). Null when
    /// nothing on the album carries a year at all.
    /// </summary>
    private string? YearLabel
    {
        get
        {
            var years = _musicFileList
                .Select(t => t.Year)
                .Where(y => y > 0)
                .ToList();

            if (years.Count == 0)
            {
                return null;
            }

            var first = years.Min();
            var last = years.Max();

            return first == last ? first.ToString() : $"{first}–{last}";
        }
    }

    // Tracks with no disc tag at all come through as disc 0; fall back to the group's position so
    // a partially-tagged multi-disc album still reads "CD 1 / CD 2" rather than "CD 0".
    private static string DiscLabel(int index, uint discNumber)
    {
        return $"CD {(discNumber == 0 ? index + 1 : discNumber)}";
    }

    private async Task ToggleFavorite()
    {
        var newValue = !_isFavorite;

        using var tokenSource = CancellationTokenSource
            .CreateLinkedTokenSource(_cts.Token);
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        await PlaatClient.SetAlbumFavorite((uint)AlbumId, newValue, tokenSource.Token);

        _isFavorite = newValue;
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

    private async Task OpenCoverPicker()
    {
        if (_coverUpload is not null && !_uploadingCover)
        {
            await _coverUpload.OpenFilePickerAsync();
        }
    }

    private async Task OnCoverImageSelected(IBrowserFile? file)
    {
        if (file is null || _uploadingCover)
        {
            return;
        }

        if (file.Size > MaxCoverImageBytes)
        {
            Snackbar.Add("That image is too large (15 MB maximum).", Severity.Error);
            return;
        }

        _uploadingCover = true;

        try
        {
            using var tokenSource = CancellationTokenSource
                .CreateLinkedTokenSource(_cts.Token);
            tokenSource.CancelAfter(Timeouts.LongTimeout);

            await using var stream = file.OpenReadStream(MaxCoverImageBytes, tokenSource.Token);

            var updated = await PlaatClient.UploadAlbumCover(
                AlbumId, stream, file.ContentType, tokenSource.Token);

            if (updated)
            {
                _coverVersion = Guid.NewGuid().ToString("N");
                Snackbar.Add("Cover image updated.", Severity.Success);
            }
            else
            {
                Snackbar.Add("Could not update the cover image: album not found.", Severity.Error);
            }
        }
        catch (Exception)
        {
            Snackbar.Add("Could not update the cover image.", Severity.Error);
        }
        finally
        {
            _uploadingCover = false;
        }
    }

    private string GetImg()
    {
        return _coverVersion.Length == 0
            ? $"/proxy/cover/album/{AlbumId}"
            : $"/proxy/cover/album/{AlbumId}?v={_coverVersion}";
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        GC.SuppressFinalize(this);
    }
}
