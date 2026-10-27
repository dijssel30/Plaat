using Microsoft.Extensions.Logging;
using Plaat.Mobile.Services.Navigation;
using Plaat.Mobile.ViewModels.Player;

namespace Plaat.Mobile.Views.Player;

public partial class MiniPlayerBar : ContentView
{
    private readonly MiniPlayerBarViewModel _viewModel;
    private readonly ILogger<MiniPlayerBar> _logger;
    private bool _subscribed;

    public MiniPlayerBar()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.GetRequiredService<MiniPlayerBarViewModel>();
        _logger = ServiceHelper.GetRequiredService<ILogger<MiniPlayerBar>>();
        BindingContext = _viewModel;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // A Shell tab's ShellContent page (Home, Albums, ...) is created once and kept alive across tab
    // switches - only its visual tree gets detached/reattached, firing Unloaded then Loaded again
    // every time the user leaves and returns to that tab. So the ViewModel's subscription to the
    // app-wide PlayerQueueService must toggle in lockstep with Loaded/Unloaded rather than being torn
    // down once - a one-shot unsubscribe left the bar permanently stale after the first tab switch
    // away. Pages that ARE truly destroyed (pushed routes like AlbumDetailPage) simply never fire
    // Loaded again, so this stays leak-free for those too.
    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_subscribed)
        {
            return;
        }

        _subscribed = true;
        _viewModel.Subscribe();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!_subscribed)
        {
            return;
        }

        _subscribed = false;
        _viewModel.Unsubscribe();
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        _viewModel.TogglePlayPause();
    }

    private void OnNextClicked(object? sender, EventArgs e)
    {
        _viewModel.Next();
    }

    private async void OnBarTapped(object? sender, TappedEventArgs e)
    {
        if (!_viewModel.HasQueue)
        {
            return;
        }

        try
        {
            await ShellNavigation.GoToNowPlayingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to now playing page");
        }
    }
}
