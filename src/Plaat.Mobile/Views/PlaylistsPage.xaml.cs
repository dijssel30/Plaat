using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class PlaylistsPage : ContentPage
{
    private readonly PlaylistsViewModel _viewModel;
    private readonly ILogger<PlaylistsPage> _logger;

    public PlaylistsPage(PlaylistsViewModel viewModel, ILogger<PlaylistsPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Always reload (unlike the other tabs): playlists get edited from elsewhere in the app -
        // tracks added via any track list's actions sheet - and the list is small, so a stale track
        // count isn't worth the saved request.
        _ = _viewModel.LoadCommand.ExecuteAsync(null);
    }

    private async void OnPlaylistTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is PlaylistOnlyDto playlist)
            {
                await Shell.Current.GoToAsync($"playlistdetail?name={Uri.EscapeDataString(playlist.PlaylistName)}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to playlist detail page");
        }
    }
}
