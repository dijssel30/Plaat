using Microsoft.Extensions.Logging;

namespace Plaat.Mobile.Views;

/// <summary>
/// The "Other" TabBar entry: a plain menu of the screens that don't warrant their own bottom-nav
/// tab (All Tracks, Search) plus Log out. Everything here is pushed onto the tab's own navigation
/// stack via Shell routes registered in AppShell. Favorites and Recently Added are reached via the
/// toolbar toggles on the Albums/Artists pages instead of a menu entry here, and Queue via the mini
/// player / now playing screen.
/// </summary>
public partial class OtherPage : ContentPage
{
    private readonly ILogger<OtherPage> _logger;

    public OtherPage(ILogger<OtherPage> logger)
    {
        InitializeComponent();
        _logger = logger;
    }

    private Task NavigateAsync(string route)
    {
        try
        {
            return Shell.Current.GoToAsync(route);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to {Route}", route);
            return Task.CompletedTask;
        }
    }

    private async void OnAllTracksTapped(object? sender, TappedEventArgs e)
    {
        await NavigateAsync("tracks");
    }

    private async void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        await NavigateAsync("search");
    }

    private async void OnLogoutTapped(object? sender, EventArgs e)
    {
        if (Shell.Current is AppShell appShell)
        {
            await appShell.LogoutAsync();
        }
    }
}
