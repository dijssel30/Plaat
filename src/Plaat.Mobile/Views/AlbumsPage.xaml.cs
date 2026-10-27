using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class AlbumsPage : ContentPage
{
    protected readonly AlbumsViewModel ViewModel;
    private readonly ILogger<AlbumsPage> _logger;

    public AlbumsPage(AlbumsViewModel viewModel, ILogger<AlbumsPage> logger)
    {
        InitializeComponent();

        ViewModel = viewModel;
        BindingContext = ViewModel;
        _logger = logger;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!ViewModel.HasLoadedOnce)
        {
            _ = ViewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private async void OnAlbumTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is AlbumOnlyDto album)
            {
                await Shell.Current.GoToAsync($"albumdetail?id={album.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to album detail page");
        }
    }
}
