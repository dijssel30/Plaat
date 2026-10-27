namespace Plaat.Mobile.Services.Navigation;

/// <summary>
/// Bridges a "open the full-screen player" request originating outside Shell's own navigation
/// (tapping the playback notification while the app is cold - not yet running) into a Shell
/// navigation. That request can arrive before Shell has finished its own async startup routing
/// (auth check -> "//home"), so it's recorded here and consumed once that routing settles, rather
/// than navigated immediately. When the app is already running and foregrounded (warm start via
/// Android's OnNewIntent), platform code navigates directly via Shell.Current instead of going
/// through this service, since Shell is guaranteed already up in that case.
/// </summary>
public sealed class AppLaunchIntentService
{
    private bool _pendingNowPlaying;

    public void RequestNowPlaying()
    {
        _pendingNowPlaying = true;
    }

    public bool ConsumePendingNowPlaying()
    {
        if (!_pendingNowPlaying)
        {
            return false;
        }

        _pendingNowPlaying = false;
        return true;
    }
}
