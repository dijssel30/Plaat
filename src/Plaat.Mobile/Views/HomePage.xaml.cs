using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel _viewModel;
    private readonly ILogger<HomePage> _logger;

    // TapGestureRecognizer and TouchBehavior.LongPressCompleted are independent recognizers: on
    // release after a long press, the tap gesture still fires (it only cares about press+release
    // within the view, not duration), which would start playback right after opening the sheet.
    // This flag, set when a long press completes, makes the following Tapped a no-op.
    private bool _suppressNextTap;

    public HomePage(HomeViewModel viewModel, PlaybackEngine playbackEngine, ILogger<HomePage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;

        playbackEngine.AttachMediaElement(SharedMediaElement);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_viewModel.HasLoadedOnce)
        {
            _ = _viewModel.LoadCommand.ExecuteAsync(null);
        }
    }

    private void OnRecentlyPlayedTapped(object? sender, TappedEventArgs e)
    {
        if (ConsumeSuppressedTap())
        {
            return;
        }

        if (e.Parameter is TrackDto track)
        {
            _viewModel.PlayFromRecentlyPlayed(track);
        }
    }

    private void OnRecentlyAddedTapped(object? sender, TappedEventArgs e)
    {
        if (ConsumeSuppressedTap())
        {
            return;
        }

        if (e.Parameter is TrackDto track)
        {
            _viewModel.PlayFromRecentlyAdded(track);
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

    private void OnTrackLongPressed(object? sender, LongPressCompletedEventArgs e)
    {
        _suppressNextTap = true;

        if (e.LongPressCommandParameter is TrackDto track)
        {
            _ = ActionsSheet.ShowAsync(track);
        }
    }

    private bool ConsumeSuppressedTap()
    {
        if (!_suppressNextTap)
        {
            return false;
        }

        _suppressNextTap = false;
        return true;
    }
}
