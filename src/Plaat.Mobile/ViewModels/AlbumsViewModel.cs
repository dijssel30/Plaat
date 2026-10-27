using CommunityToolkit.Mvvm.Input;
using Plaat.Contract.Client;
using Plaat.Contract.Extensions;
using Plaat.Contract.Models;
using Plaat.Mobile.Collections;

namespace Plaat.Mobile.ViewModels;

public partial class AlbumsViewModel(IPlaatClient plaatClient) : ViewModelBase
{
    private enum AlbumsFilter
    {
        All,
        Favorites,
        RecentlyAdded
    }

    private List<AlbumOnlyDto> _allAlbums = [];
    private AlbumsFilter _filter = AlbumsFilter.All;

    /// <summary>
    /// Toggleable via the Albums page's toolbar buttons - mirrors Plaat.Frontend's Albums.razor,
    /// which handles "/albums", "/albums/favorites" and "/albums/recently-added" as one component,
    /// via the same toggle buttons. Only one of favorites/recently-added can be active at a time.
    /// </summary>
    public bool FavoritesOnly
    {
        get
        {
            return _filter == AlbumsFilter.Favorites;
        }
    }

    public bool RecentlyAddedOnly
    {
        get
        {
            return _filter == AlbumsFilter.RecentlyAdded;
        }
    }

    public string FavoritesToggleLabel
    {
        get
        {
            return FavoritesOnly ? "★ All albums" : "☆ Favorites";
        }
    }

    public string RecentlyAddedToggleLabel
    {
        get
        {
            return RecentlyAddedOnly ? "All albums" : "Recently Added";
        }
    }

    public BulkObservableCollection<AlbumOnlyDto> Albums { get; } = [];

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

    private void SetFilter(AlbumsFilter filter)
    {
        if (_filter == filter)
        {
            return;
        }

        _filter = filter;
        OnPropertyChanged(nameof(FavoritesOnly));
        OnPropertyChanged(nameof(RecentlyAddedOnly));
        OnPropertyChanged(nameof(FavoritesToggleLabel));
        OnPropertyChanged(nameof(RecentlyAddedToggleLabel));

        if (HasLoadedOnce)
        {
            _ = LoadCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private void ToggleFavorites()
    {
        SetFilter(FavoritesOnly ? AlbumsFilter.All : AlbumsFilter.Favorites);
    }

    [RelayCommand]
    private void ToggleRecentlyAdded()
    {
        SetFilter(RecentlyAddedOnly ? AlbumsFilter.All : AlbumsFilter.RecentlyAdded);
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
        Title = _filter switch
        {
            AlbumsFilter.Favorites => "Favorite Albums",
            AlbumsFilter.RecentlyAdded => "Recently Added Albums",
            _ => "Albums"
        };

        try
        {
            var albums = _filter switch
            {
                AlbumsFilter.Favorites => await plaatClient.GetFavoriteAlbums(CancellationToken.None),
                AlbumsFilter.RecentlyAdded => (await plaatClient.GetRecentlyAdded(CancellationToken.None)).Albums,
                _ => await plaatClient.GetAlbums(CancellationToken.None)
            };

            _allAlbums = [.. albums.OrderBy(a => a.Title, StringComparer.OrdinalIgnoreCase)];
            ApplyFilter();
            HasLoadedOnce = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't load albums: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = string.IsNullOrWhiteSpace(Search)
            ? _allAlbums
            : [.. _allAlbums
                .Select(a => (Item: a, Score: SearchScoring.SearchScore(Search, a.Title, a.AllArtists)))
                .Where(x => x.Score is not null)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Item)];

        Albums.ReplaceAll(filtered);
    }
}
