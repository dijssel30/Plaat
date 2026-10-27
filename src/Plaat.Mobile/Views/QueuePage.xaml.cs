using Microsoft.Extensions.Logging;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class QueuePage : ContentPage
{
    private readonly QueueViewModel _viewModel;
    private readonly ILogger<QueuePage> _logger;

    private VisualElement? _dragRow;
    private int _dragStartIndex;
    private int _dragCurrentIndex;
    private double _dragRowHeight;

    public QueuePage(QueueViewModel viewModel, ILogger<QueuePage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;

        // OnAppearing fires before the CollectionView has measured/realized its items, so an
        // immediate ScrollTo there is silently ignored - Loaded is the point it's actually ready.
        TracksCollectionView.Loaded += (_, _) => ScrollToCurrentTrack();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Subscribe();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Unsubscribe();
    }

    private void ScrollToCurrentTrack()
    {
        var index = _viewModel.CurrentIndex;
        if (index >= 0 && index < _viewModel.Tracks.Count)
        {
            TracksCollectionView.ScrollTo(index, position: ScrollToPosition.Start, animate: false);
        }
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate back from queue page");
        }
    }

    private void OnTrackTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is QueueTrackItem track)
        {
            _viewModel.PlayTrack(track);
        }
    }

    private void OnRemoveInvoked(object? sender, EventArgs e)
    {
        if (sender is SwipeItem { CommandParameter: QueueTrackItem track })
        {
            _viewModel.RemoveTrack(track);
        }
    }

    /// <summary>
    /// Hand-rolled drag-to-reorder driven by the handle's PanGestureRecognizer. See the comment in
    /// QueuePage.xaml for why: DragGestureRecognizer/DropGestureRecognizer don't fire on Android.
    /// While dragging, sibling rows are live-reordered in the page's own Tracks copy (not the real
    /// queue) so the list visibly opens a slot as you cross each row boundary; the real queue only
    /// gets one Move call, on release.
    /// </summary>
    private void OnHandlePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Element { Parent: VisualElement row, BindingContext: QueueTrackItem item })
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragRow = row;
                _dragStartIndex = _dragCurrentIndex = _viewModel.Tracks.IndexOf(item);
                _dragRowHeight = row.Height > 0 ? row.Height : 64;
                row.ZIndex = 1;
                row.Opacity = 0.9;
                row.Scale = 1.02;
                SetElevated(row, true);
                break;

            case GestureStatus.Running:
                if (ReferenceEquals(_dragRow, row))
                {
                    var wantedIndex = Math.Clamp(
                        _dragStartIndex + (int)Math.Round(e.TotalY / _dragRowHeight),
                        0,
                        _viewModel.Tracks.Count - 1);
                    if (wantedIndex != _dragCurrentIndex)
                    {
                        _viewModel.Tracks.Move(_dragCurrentIndex, wantedIndex);
                        _dragCurrentIndex = wantedIndex;
                    }

                    // Residual offset within the current slot, so the row keeps tracking the finger
                    // smoothly between the snap points instead of jumping back to 0 after each Move.
                    row.TranslationY = e.TotalY - (_dragCurrentIndex - _dragStartIndex) * _dragRowHeight;
                }
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (ReferenceEquals(_dragRow, row))
                {
                    CommitDrag();
                }
                break;
        }
    }

    private void CommitDrag()
    {
        var row = _dragRow;
        var fromIndex = _dragStartIndex;
        var toIndex = _dragCurrentIndex;
        _dragRow = null;

        if (row is null)
        {
            return;
        }

        row.TranslationY = 0;
        row.Opacity = 1;
        row.Scale = 1;
        row.ZIndex = 0;
        SetElevated(row, false);

        _viewModel.CommitDrag(fromIndex, toIndex);
    }

    private static void SetElevated(VisualElement row, bool elevated)
    {
#if ANDROID
        // MAUI's ZIndex only reorders children within the same layout - a CollectionView row is its
        // own top-level item view in the native RecyclerView, so making the dragged row actually draw
        // over its neighbors (instead of under them) needs real Android elevation.
        if (row.Handler?.PlatformView is global::Android.Views.View platformView)
        {
            platformView.Elevation = elevated ? 24 : 0;
        }
#endif
    }
}
