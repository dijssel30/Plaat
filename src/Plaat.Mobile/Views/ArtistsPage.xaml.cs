using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class ArtistsPage : ContentPage
{
    private readonly ArtistsViewModel _viewModel;
    private readonly ILogger<ArtistsPage> _logger;

    public ArtistsPage(ArtistsViewModel viewModel, ILogger<ArtistsPage> logger)
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

    private async void OnArtistTapped(object? sender, TappedEventArgs e)
    {
        // Reads the artist off the tapped row's own BindingContext rather than e.Parameter: under
        // CollectionView cell recycling (this list resets wholesale on every search keystroke via
        // BulkObservableCollection.ReplaceAll), a TapGestureRecognizer's CommandParameter binding can
        // lag a recycled row by one rebind, silently opening the previous occupant of that row - most
        // noticeable with same-named artists, where the stale target still looks right. sender is the
        // actual Grid that was tapped, so its BindingContext is always the row currently on screen.
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
}
