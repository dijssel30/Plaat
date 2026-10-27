using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;

namespace Plaat.Mobile.ViewModels;

public partial class ArtistsViewModel(IPlaatClient plaatClient) : ViewModelBase
{
    private List<ArtistOnlyDto> _allArtists = [];

    /// <summary>
    /// Toggleable via the Artists page's toolbar button - mirrors Plaat.Frontend's Artists.razor,
    /// which handles "/artists" and "/artists/favorites" as one component, and AlbumsViewModel's
    /// FavoritesOnly toggle.
    /// </summary>
    [ObservableProperty]
    public partial bool FavoritesOnly { get; set; }

    public string FavoritesToggleLabel
    {
        get
        {
            return FavoritesOnly ? "★ All artists" : "☆ Favorites";
        }
    }

    public BulkObservableCollection<ArtistOnlyDto> Artists { get; } = [];

    public string Search
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                ApplyFilter();
            }
        }
    } = string.Empty;

    partial void OnFavoritesOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoritesToggleLabel));

        if (HasLoadedOnce)
        {
            _ = LoadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void ToggleFavorites()
    {
        FavoritesOnly = !FavoritesOnly;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        Title = FavoritesOnly ? "Favorite Artists" : "Artists";

        try
        {
            var artists = FavoritesOnly
                ? await plaatClient.GetFavoriteArtists(CancellationToken.None)
                : await plaatClient.GetArtists(CancellationToken.None);

            _allArtists = [.. artists.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];
            ApplyFilter();
            HasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load artists: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(Search)
            ? _allArtists
            : [.. _allArtists
                .Select(a => (Item: a, Score: SearchScoring.SearchScore(Search, a.Name)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];

        Artists.ReplaceAll(filtered);
    }
}
