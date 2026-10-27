using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class ArtistDetailPage : ContentPage
{
    private readonly ArtistDetailViewModel _viewModel;
    private readonly ILogger<ArtistDetailPage> _logger;

    // See the comment in HomePage.xaml.cs: without this, releasing a long press also fires the
    // row's Tapped and starts playback right after opening the sheet.
    private bool _suppressNextTap;

    public ArtistDetailPage(ArtistDetailViewModel viewModel, ILogger<ArtistDetailPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    private void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (TappedTrack(e) is { } track)
        {
            _viewModel.PlayTrack(track);
        }
    }

    private void OnPopularTrackTapped(object? sender, TappedEventArgs e)
    {
        if (TappedTrack(e) is { } track)
        {
            _viewModel.PlayPopularTrack(track);
        }
    }

    private TrackDto? TappedTrack(TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return null;
        }

        return e.Parameter as TrackDto;
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

    private void OnTrackLongPressed(object? sender, LongPressCompletedEventArgs e)
    {
        _suppressNextTap = true;

        if (e.LongPressCommandParameter is TrackDto track)
        {
            _ = ActionsSheet.ShowAsync(track);
        }
    }
}
