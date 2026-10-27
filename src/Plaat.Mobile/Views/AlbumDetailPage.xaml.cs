using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class AlbumDetailPage : ContentPage
{
    private readonly AlbumDetailViewModel _viewModel;
    private readonly ILogger<AlbumDetailPage> _logger;

    // See the comment in PlaylistDetailPage.xaml.cs / HomePage.xaml.cs: without this, releasing a
    // long press also fires the row's Tapped and starts playback right after opening the sheet.
    private bool _suppressNextTap;

    public AlbumDetailPage(AlbumDetailViewModel viewModel, ILogger<AlbumDetailPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    private void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return;
        }

        if (e.Parameter is TrackDto track)
        {
            _viewModel.PlayTrack(track);
        }
    }

    private async void OnArtistTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_viewModel.PrimaryArtistId is uint artistId)
            {
                await Shell.Current.GoToAsync($"artistdetail?id={artistId}");
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
