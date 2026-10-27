using CommunityToolkit.Maui.Core;
using Plaat.Contract;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class TracksPage : ContentPage
{
    private readonly TracksViewModel _viewModel;

    // See the comment in HomePage.xaml.cs: without this, releasing a long press also fires the
    // row's Tapped and starts playback right after opening the sheet.
    private bool _suppressNextTap;

    public TracksPage(TracksViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
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

        if (e.Parameter is TrackDto track)
        {
            _viewModel.PlayTrack(track);
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
