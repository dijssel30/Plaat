using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Albums : ComponentBase
{
    private const int MinCoverSize = 100;
    private const int MaxCoverSize = 300;
    private const int DefaultCoverSize = 180;
    private const int GridGap = 16;

    /// <summary>
    /// Number of albums per virtualized chunk. Each chunk renders as its own CSS `auto-fill`
    /// grid, so the browser still handles column wrapping exactly as before - this only bounds
    /// how many tiles Blazor has to render/diff/transmit at once for a huge library.
    /// </summary>
    private const int AlbumBatchSize = 48;

    /// <summary>
    /// Rough assumed content width, used only to estimate each chunk's height for Virtualize's
    /// scroll math. Getting this wrong doesn't break layout (the real grid still wraps based on
    /// actual width) - it just makes Virtualize's prefetching slightly less precise.
    /// </summary>
    private const int AssumedContainerWidth = 1200;

    [Inject]
    public required IPlaatClient PlaatClient { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    [Inject]
    public required ProtectedLocalStorage LocalStorage { get; set; }

    [Parameter]
    public int? ArtistId { get; init; }

    private List<AlbumOnlyDto> _albumList = [];
    private string _headingLabel = "Albums";
    private bool _favoritesOnly;
    private bool _recentlyAddedOnly;

    /// <summary>
    /// Local storage key for view mode/cover size, scoped per listing so the full album list,
    /// an artist's albums and favorites don't clobber each other's prefs.
    /// </summary>
    private string _viewPrefsStorageKey = "albums_view_prefs";

    private string? _search;
    private AlbumsViewMode _viewMode = AlbumsViewMode.List;
    private int _coverSize = DefaultCoverSize;

    private List<AlbumOnlyDto> FilteredAlbumList
    {
        get
        {
            return string.IsNullOrWhiteSpace(_search)
            ? _albumList
            : [.. _albumList
                .Select(x => (Item: x, Score: SearchScoring.SearchScore(_search, x.Title, x.AllArtists)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];
        }
    }

    private List<List<AlbumOnlyDto>> AlbumBatches
    {
        get
        {
            var source = FilteredAlbumList;
            var batches = new List<List<AlbumOnlyDto>>();

            for (var i = 0; i < source.Count; i += AlbumBatchSize)
            {
                batches.Add(source.GetRange(i, Math.Min(AlbumBatchSize, source.Count - i)));
            }

            return batches;
        }
    }

    private int EstimatedBatchHeight
    {
        get
        {
            var estimatedColumns = Math.Max(1, (AssumedContainerWidth + GridGap) / (_coverSize + GridGap));
            var estimatedRows = (int)Math.Ceiling(AlbumBatchSize / (double)estimatedColumns);

            return estimatedRows * (_coverSize + 60);
        }
    }

    private static string GetHref(uint id)
    {
        return Redirects.GetAlbumHref(id);
    }

    private static string GetCoverImg(ulong albumId)
    {
        return $"/proxy/cover/album/{albumId}";
    }

    protected override async Task OnInitializedAsync()
    {
        var relativePath = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);

        // Favorites-only/recently-added-only start on whenever the page was reached via their
        // respective deep links; from then on it's driven purely by the toggle buttons (see
        // ToggleFavoritesOnly/ToggleRecentlyAddedOnly), which also keep the URL in sync so
        // refreshing the page preserves the choice. The two are mutually exclusive.
        _favoritesOnly = ArtistId is null
            && relativePath.StartsWith("albums/favorites", StringComparison.OrdinalIgnoreCase);
        _recentlyAddedOnly = ArtistId is null
            && relativePath.StartsWith("albums/recently-added", StringComparison.OrdinalIgnoreCase);

        await LoadAlbums();
    }

    private async Task LoadAlbums()
    {
        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        if (_favoritesOnly)
        {
            var favorites = await PlaatClient.GetFavoriteAlbums(tokenSource.Token);

            _albumList = [.. favorites.OrderBy(l => l.Title)];
            _headingLabel = "Favorite Albums";
            _viewPrefsStorageKey = "favorite_albums_view_prefs";
        }
        else if (_recentlyAddedOnly)
        {
            var recentlyAdded = await PlaatClient.GetRecentlyAdded(tokenSource.Token);

            _albumList = [.. recentlyAdded.Albums.OrderBy(l => l.Title)];
            _headingLabel = "Recently Added Albums";
            _viewPrefsStorageKey = "recently_added_albums_view_prefs";
        }
        else if (ArtistId is null)
        {
            var music = await PlaatClient.GetAlbums(tokenSource.Token);

            _albumList = [.. music.OrderBy(l => l.Title)];
            _headingLabel = "Albums";
            _viewPrefsStorageKey = "albums_view_prefs";
        }
        else
        {
            var artist = await PlaatClient.GetArtist(ArtistId.Value, tokenSource.Token)
                         ?? throw new Exception("Artist not found");

            _albumList = [.. artist.Albums];
        }
    }

    private async Task ToggleFavoritesOnly()
    {
        _favoritesOnly = !_favoritesOnly;
        if (_favoritesOnly)
        {
            _recentlyAddedOnly = false;
        }

        await LoadAlbums();
        NavigationManager.NavigateTo(_favoritesOnly ? "/albums/favorites" : "/albums", replace: true);
    }

    private async Task ToggleRecentlyAddedOnly()
    {
        _recentlyAddedOnly = !_recentlyAddedOnly;
        if (_recentlyAddedOnly)
        {
            _favoritesOnly = false;
        }

        await LoadAlbums();
        NavigationManager.NavigateTo(_recentlyAddedOnly ? "/albums/recently-added" : "/albums", replace: true);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        var prefs = await LoadViewPrefs();
        if (prefs is null)
        {
            return;
        }

        _viewMode = prefs.ViewMode;
        _coverSize = Math.Clamp(prefs.CoverSize, MinCoverSize, MaxCoverSize);
        StateHasChanged();
    }

    private async Task<AlbumsViewPrefs?> LoadViewPrefs()
    {
        try
        {
            var result = await LocalStorage.GetAsync<AlbumsViewPrefs>(_viewPrefsStorageKey);
            return result.Success ? result.Value : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveViewPrefs()
    {
        try
        {
            await LocalStorage.SetAsync(_viewPrefsStorageKey, new AlbumsViewPrefs(_viewMode, _coverSize));
        }
        catch
        {
            // View preferences are a non-critical convenience; ignore storage failures.
        }
    }

    private async Task SetViewMode(AlbumsViewMode mode)
    {
        if (_viewMode == mode)
        {
            return;
        }

        _viewMode = mode;
        await SaveViewPrefs();
    }

    private async Task OnCoverSizeChanged(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var size))
        {
            _coverSize = Math.Clamp(size, MinCoverSize, MaxCoverSize);
            await SaveViewPrefs();
        }
    }

    private enum AlbumsViewMode
    {
        List,
        Grid
    }

    private sealed record AlbumsViewPrefs(AlbumsViewMode ViewMode, int CoverSize);
}
