using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class SearchPage : ContentPage
{
    private readonly SearchViewModel _viewModel;
    private readonly ILogger<SearchPage> _logger;

    // See the comment in HomePage.xaml.cs: without this, releasing a long press also fires the
    // row's Tapped and starts playback right after opening the sheet.
    private bool _suppressNextTap;

    public SearchPage(SearchViewModel viewModel, ILogger<SearchPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_viewModel.HasLoadedOnce)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return;
        }

        // See OnArtistTapped: this list also resets wholesale on every search keystroke.
        if (sender is BindableObject { BindingContext: TrackDto track })
        {
            _viewModel.PlayTrack(track);
        }
    }

    private async void OnAlbumTapped(object? sender, TappedEventArgs e)
    {
        // See OnArtistTapped: this list also resets wholesale on every search keystroke.
        try
        {
            if (sender is BindableObject { BindingContext: AlbumOnlyDto album })
            {
                await Shell.Current.GoToAsync($"albumdetail?id={album.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to album detail page");
        }
    }

    private async void OnArtistTapped(object? sender, TappedEventArgs e)
    {
        // Reads the artist off the tapped row's own BindingContext rather than e.Parameter: this
        // list resets wholesale on every search keystroke via BulkObservableCollection.ReplaceAll,
        // so a TapGestureRecognizer's CommandParameter binding can lag a recycled row by one rebind
        // under CollectionView cell recycling - see the identical fix in ArtistsPage.
        try
        {
            if (sender is BindableObject { BindingContext: ArtistOnlyDto artist })
            {
                await Shell.Current.GoToAsync($"artistdetail?id={artist.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to artist detail page");
        }
    }

    private void OnTrackLongPressed(object? sender, LongPressCompletedEventArgs e)
    {
        _suppressNextTap = true;

        if (e.LongPressCommandParameter is TrackDto track)
        {
            _ = ActionsSheet.ShowAsync(track);
        }
    }
}
