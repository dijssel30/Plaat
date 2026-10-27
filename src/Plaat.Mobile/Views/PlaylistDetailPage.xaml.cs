using CommunityToolkit.Maui.Core;
using Plaat.Contract;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class PlaylistDetailPage : ContentPage
{
    private readonly PlaylistDetailViewModel _viewModel;

    // See the comment in HomePage.xaml.cs: without this, releasing a long press also fires the
    // row's Tapped and starts playback right after opening the sheet.
    private bool _suppressNextTap;

    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The first appearance is already covered by the load the "name" query property triggers.
        if (_viewModel.HasLoadedOnce)
        {
            _ = _viewModel.ReloadAsync();
        }
    }

    private void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (_suppressNextTap)
        {
            _suppressNextTap = false;
            return;
        }

        // Read the track off the tapped row's own BindingContext rather than e.Parameter: the list
        // now resets wholesale on every search keystroke (BulkObservableCollection.ReplaceAll), so a
        // TapGestureRecognizer's CommandParameter binding can lag a recycled row by one rebind - see
        // the identical fix in SearchPage/ArtistsPage.
        if (sender is BindableObject { BindingContext: TrackDto track })
        {
            _viewModel.PlayTrack(track);
        }
    }

    private void OnTrackLongPressed(object? sender, LongPressCompletedEventArgs e)
    {
        _suppressNextTap = true;

        if (e.LongPressCommandParameter is TrackDto track)
        {
            // "Remove from playlist" only makes sense for a normal playlist - a smart playlist's
            // contents are computed from its rules, so there's nothing to remove a track from.
            var playlistId = _viewModel.Playlist is { IsSmart: false } playlist ? playlist.Id : (uint?)null;
            _ = ActionsSheet.ShowAsync(track, playlistId);
        }
    }

    private void OnRemovedFromPlaylist(object? sender, TrackDto track)
    {
        _viewModel.RemoveTrackLocally(track);
    }
}
