using Microsoft.Extensions.Logging;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;
using Plaat.Mobile.Services.Auth;
using Plaat.Mobile.Services.Compatibility;
using Plaat.Mobile.Services.Navigation;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Views;
using Plaat.Mobile.Views.TrackActions;

namespace Plaat.Mobile;

public partial class AppShell : Shell
{
    private readonly ILogger<AppShell> _logger = ServiceHelper.GetRequiredService<ILogger<AppShell>>();

    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("albumdetail", typeof(AlbumDetailPage));
        Routing.RegisterRoute("artistdetail", typeof(ArtistDetailPage));
        Routing.RegisterRoute("playlistdetail", typeof(PlaylistDetailPage));
        Routing.RegisterRoute("nowplaying", typeof(NowPlayingPage));
        Routing.RegisterRoute("queue", typeof(QueuePage));
        Routing.RegisterRoute("trackdetails", typeof(TrackDetailsPage));

        // Pushed from the "Other" tab (see OtherPage) rather than being their own TabBar entries.
        Routing.RegisterRoute("tracks", typeof(TracksPage));
        Routing.RegisterRoute("search", typeof(SearchPage));

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await StartUpAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "App startup failed");
        }
    }

    private async Task StartUpAsync()
    {
        // Fire-and-forget: shows its own dismissible warning once ready, but must never delay
        // login/home navigation below - a slow or unreachable server shouldn't hold up startup.
        _ = CheckAppCompatibilityAsync();

        var tokenStorage = ServiceHelper.GetRequiredService<TokenStorageService>();
        var authService = ServiceHelper.GetRequiredService<AuthService>();

        await authService.RestoreSession(CancellationToken.None);

        if (!tokenStorage.IsAuthenticated)
        {
            await GoToAsync("//login");
            return;
        }

        await GoToAsync("//home");

        // Cold start via the playback notification's tap action: the request was recorded before
        // Shell finished this async startup routing (see AppLaunchIntentService), so it's only safe
        // to act on now that "//home" is actually the current page.
        if (ServiceHelper.GetRequiredService<AppLaunchIntentService>().ConsumePendingNowPlaying())
        {
            await ShellNavigation.GoToNowPlayingAsync();
        }

        var stateSync = ServiceHelper.GetRequiredService<PlaybackStateSyncService>();

        // Rebuild the queue from the local cache first (instant, off disk) so the mini player
        // renders right away, then reconcile with the backend-persisted state over the network.
        await stateSync.RestoreFromCacheAsync();
        await stateSync.RestoreAsync(CancellationToken.None);
        stateSync.StartSyncLoop();
    }

    // Runs at most once per app process launch, since OnLoaded (its only caller path, via
    // StartUpAsync) unsubscribes itself immediately - so this alert can only ever appear once per
    // fresh startup, never repeatedly during a session.
    private async Task CheckAppCompatibilityAsync()
    {
        try
        {
            var plaatClient = ServiceHelper.GetRequiredService<IPlaatClient>();
            var compatibility = await plaatClient.GetAppCompatibility(CancellationToken.None);
            var currentVersion = AppInfo.Current.VersionString;

            if (compatibility is null || AppCompatibilityChecker.IsCompatible(currentVersion, compatibility))
            {
                return;
            }

            await Current.DisplayAlertAsync(
                "Compatibility warning",
                $"This app version ({currentVersion}) may not be fully compatible with the server. Some features might not work correctly.",
                "Continue anyway");
        }
        catch (Exception ex)
        {
            // Best-effort: an unreachable server or an old backend without this endpoint should
            // never block or interrupt startup.
            _logger.LogWarning(ex, "App compatibility check failed");
        }
    }

    public async Task LogoutAsync()
    {
        try
        {
            var authService = ServiceHelper.GetRequiredService<AuthService>();
            var queue = ServiceHelper.GetRequiredService<PlayerQueueService>();

            queue.Clear();
            ServiceHelper.GetRequiredService<QueueStateStore>().Delete();
            await authService.Logout();
            await GoToAsync("//login");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout failed");
        }
    }
}
