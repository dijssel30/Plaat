using Microsoft.AspNetCore.Components;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Pages;

public partial class Artists : ComponentBase
{
    [Inject]
    public required IPlaatClient PlaatClient { get; set; }

    [Inject]
    public required NavigationManager NavigationManager { get; set; }

    private List<ArtistOnlyDto> _artistList = [];
    private string? _search;
    private string _headingLabel = "Artists";
    private bool _favoritesOnly;

    private List<ArtistOnlyDto> FilteredArtistList
    {
        get
        {
            return string.IsNullOrWhiteSpace(_search)
            ? _artistList
            : [.. _artistList
                .Select(x => (Item: x, Score: SearchScoring.SearchScore(_search, x.Name)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];
        }
    }

    private static string GetHref(ulong id)
    {
        return $"Artists/{id}";
    }

    protected override async Task OnInitializedAsync()
    {
        var relativePath = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);

        // Favorites-only starts on whenever the page was reached via the /artists/favorites deep
        // link; from then on it's driven purely by the toggle button (see ToggleFavoritesOnly),
        // which also keeps the URL in sync so refreshing the page preserves the choice.
        _favoritesOnly = relativePath.StartsWith("artists/favorites", StringComparison.OrdinalIgnoreCase);

        await LoadArtists();
    }

    private async Task LoadArtists()
    {
        using var tokenSource = new CancellationTokenSource();
        tokenSource.CancelAfter(Timeouts.NormalTimeout);

        var artists = _favoritesOnly
            ? await PlaatClient.GetFavoriteArtists(tokenSource.Token)
            : await PlaatClient.GetArtists(tokenSource.Token);

        _artistList = [.. artists.OrderBy(l => l.Name)];
        _headingLabel = _favoritesOnly ? "Favorite Artists" : "Artists";
    }

    private async Task ToggleFavoritesOnly()
    {
        _favoritesOnly = !_favoritesOnly;
        await LoadArtists();
        NavigationManager.NavigateTo(_favoritesOnly ? "/artists/favorites" : "/artists", replace: true);
    }
}
