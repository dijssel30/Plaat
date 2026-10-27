using Microsoft.Extensions.Logging;
using Plaat.Contract.Models;
using Plaat.Mobile.Services.Navigation;
using Plaat.Mobile.ViewModels;

namespace Plaat.Mobile.Views;

public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly ILogger<NowPlayingPage> _logger;

    private bool _swipeToDismissWired;

    // How far the finger has to travel across the star row before the press counts as a swipe
    // rather than a tap on the star it started on.
    private const double RatingDragThresholdPx = 8;

    private bool _ratingGestureActive;
    private double _ratingPressX;
    private bool _ratingMoved;
    private int? _ratingPreview;

    public NowPlayingPage(NowPlayingViewModel viewModel, ILogger<NowPlayingPage> logger)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    /// <summary>
    /// Mirrors the root grid's swipe-to-dismiss onto everything drawn inside it. Android only hands
    /// a touch to a parent view when no child claimed it first, so a swipe that starts on the cover
    /// art (which has its own tap gesture) or on any other content never reached the root grid -
    /// dismissing only worked over the page's empty spots, mostly the strip above the artwork.
    /// The playback controls are left out - a drag on the seek bar or a press on a transport button
    /// is their own gesture, and shouldn't double as a dismiss - and so is every container between
    /// them and the root: a SwipeGestureRecognizer on a container still competes for the same touch
    /// stream as a control it holds, and it was the Slider's immediate StackLayout carrying one -
    /// not the Slider itself, which was already skipped - that broke seek-bar dragging. Any touch
    /// that lands in one of these now-unclaimed containers still dismisses, because Android falls
    /// back to the nearest ancestor that has a recognizer once no child there claims it, so nothing
    /// above them needs to be excluded too.
    ///
    /// Wired from <see cref="OnNavigatedTo"/> (once), not the constructor: walking the whole visual
    /// tree and hanging a <see cref="SwipeGestureRecognizer"/> off ~15-20 views is enough main-thread
    /// work that, when it ran inline with the modal open, the frame it landed on overran and the
    /// audio output (a plain in-process ExoPlayer - no foreground-service thread-priority boost,
    /// MauiProgram's isAndroidForegroundServiceEnabled: false) dropped a buffer, so playback hitched
    /// for a moment on every open. OnNavigatedTo fires after the present animation has finished, off
    /// that critical frame; the swipe is a fraction of a second late to arm, which is invisible (you
    /// can't swipe mid-animation anyway).
    /// </summary>
    private void AddSwipeToDismissToContent()
    {
        var protectedAncestors = ProtectedAncestors();

        foreach (var view in Content(RootGrid))
        {
            if (HandlesOwnTouches(view) || IsInsideStarsRow(view) || protectedAncestors.Contains(view))
            {
                continue;
            }

            var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Down };
            swipe.Swiped += OnSwipedDown;
            view.GestureRecognizers.Add(swipe);
        }

        // StarsRow is treated like the controls: it runs its own pointer gesture (rating by tap or
        // swipe), so neither it, nor its containers, nor the star images inside it may carry a
        // SwipeGestureRecognizer. The images matter most - a star with its own recognizer claims the
        // touch before StarsRow ever sees it, so only presses landing in the gaps between stars
        // used to reach the rating handlers.
        bool HandlesOwnTouches(View view)
        {
            return view is Slider or ImageButton or Button || view == StarsRow;
        }

        bool IsInsideStarsRow(View view)
        {
            for (var ancestor = view.Parent as View; ancestor is not null; ancestor = ancestor.Parent as View)
            {
                if (ancestor == StarsRow)
                {
                    return true;
                }
            }

            return false;
        }

        HashSet<View> ProtectedAncestors()
        {
            var result = new HashSet<View>();

            foreach (var control in Content(RootGrid).Where(HandlesOwnTouches))
            {
                for (var ancestor = control.Parent as View; ancestor is not null && ancestor != RootGrid; ancestor = ancestor.Parent as View)
                {
                    result.Add(ancestor);
                }
            }

            return result;
        }

        static IEnumerable<View> Content(View view)
        {
            IEnumerable<View> children = view switch
            {
                Layout layout => layout.Children.OfType<View>(),
                ContentView { Content: View content } => [content],
                Border { Content: View content } => [content],
                _ => []
            };

            foreach (var child in children)
            {
                yield return child;

                foreach (var descendant in Content(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>
    /// Star rating by touch: a tap sets the star it lands on (or clears the rating when that star is
    /// the one already set), and a swipe across the row follows the finger, including off the left
    /// edge to clear. Mirrors the web frontend's player, whose stars behave the same way.
    /// </summary>
    private void OnStarsPointerPressed(object? sender, PointerEventArgs e)
    {
        if (e.GetPosition(StarsRow) is not { } position)
        {
            return;
        }

        _ratingGestureActive = true;
        _ratingMoved = false;
        _ratingPressX = position.X;
        _ratingPreview = null;
    }

    private void OnStarsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_ratingGestureActive || e.GetPosition(StarsRow) is not { } position)
        {
            return;
        }

        if (!_ratingMoved && Math.Abs(position.X - _ratingPressX) < RatingDragThresholdPx)
        {
            return;
        }

        _ratingMoved = true;

        var stars = StarsAt(position.X);

        if (stars == _ratingPreview)
        {
            return;
        }

        _ratingPreview = stars;
        _viewModel.PaintStars(stars);
    }

    private async void OnStarsPointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_ratingGestureActive)
        {
            return;
        }

        _ratingGestureActive = false;

        var swipedTo = _ratingMoved ? _ratingPreview : null;
        _ratingPreview = null;

        // A swipe lands wherever it ended; a tap takes the star it pressed, and clears the rating
        // when that is the star already set.
        var pressed = StarsAt(_ratingPressX);
        var stars = swipedTo ?? (pressed == _viewModel.Rating ? TrackRatingDto.MinStars : pressed);

        if (stars == _viewModel.Rating)
        {
            _viewModel.PaintStars(_viewModel.Rating);
            return;
        }

        try
        {
            await _viewModel.SetRating(stars);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the star rating");
        }
    }

    /// <summary>
    /// The rating a position across the star row stands for. The row is split into
    /// <see cref="TrackRatingDto.MaxStars"/> equal bands, and anything at or left of its start reads
    /// as 0, so swiping off the left edge clears the rating.
    /// </summary>
    private int StarsAt(double x)
    {
        if (StarsRow.Width <= 0)
        {
            return _viewModel.Rating;
        }

        var relative = Math.Clamp(x / StarsRow.Width, 0, 1);

        return (int)Math.Ceiling(relative * TrackRatingDto.MaxStars);
    }

    private void OnSeekStarted(object? sender, EventArgs e)
    {
        _viewModel.BeginSeek();
    }

    private void OnSeekValueChanged(object? sender, ValueChangedEventArgs e)
    {
        _viewModel.SliderValueChanged(e.NewValue);
    }

    private async void OnSeekCompleted(object? sender, EventArgs e)
    {
        try
        {
            if (sender is Slider slider)
            {
                await _viewModel.EndSeek(slider.Value);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seek to new position");
        }
    }

    private async void OnQueueClicked(object? sender, EventArgs e)
    {
        try
        {
            await ShellNavigation.GoToQueueAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to queue page");
        }
    }

    private async void OnCoverArtTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_viewModel.AlbumId is uint albumId)
            {
                await NavigateAwayFromPlayer($"albumdetail?id={albumId}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to album detail page");
        }
    }

    private async void OnArtistTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is NowPlayingArtist artist)
            {
                await NavigateAwayFromPlayer($"artistdetail?id={artist.Id}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to artist detail page");
        }
    }

    /// <summary>
    /// Closes this modal and pushes the target route in the same navigation, instead of pushing it
    /// directly on top. NowPlayingPage is shown with Shell.PresentationMode="ModalAnimated", and
    /// Shell keeps pushing further routes onto that same modal stack rather than the normal tab
    /// stack - so a direct GoToAsync here left the artist/album page stuck under NowPlaying's
    /// hidden nav bar, and with NowPlayingPage still sitting underneath in the modal stack,
    /// ShellNavigation's "already open" guard made the mini player's tap silently no-op instead of
    /// reopening it. Two separate awaited GoToAsync calls (pop, then push) fixed that but broke
    /// every navigation afterwards - Shell doesn't tolerate a second GoToAsync starting before the
    /// first's animation has finished, and failed silently since nothing here surfaces ILogger
    /// output on Android. "../route" performs both steps as a single navigation. Album art and
    /// artist names are meant to leave the player and land in the main app, unlike the queue button
    /// (see QueuePage's header comment) which deliberately stays inside it.
    /// </summary>
    private static Task NavigateAwayFromPlayer(string route)
    {
        return Shell.Current.GoToAsync($"../{route}");
    }

    private async void OnCloseClicked(object? sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate back from now playing page");
        }
    }

    private async void OnSwipedDown(object? sender, SwipedEventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate back from now playing page");
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.Resume();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (!_swipeToDismissWired)
        {
            _swipeToDismissWired = true;
            AddSwipeToDismissToContent();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Dispose();
    }
}
