using Plaat.Mobile.Views;

namespace Plaat.Mobile.Services.Navigation;

/// <summary>
/// Guards against pushing a page that's already open. The full-screen player and the queue are
/// reachable from several places at once - the mini player, the playback notification, cold-start
/// routing - and Shell's <c>GoToAsync</c> happily stacks a second (or third) copy of the same modal
/// page on top of itself, so the user then has to dismiss it several times to get back. These
/// helpers no-op when the target is already the top of the modal / navigation stack.
/// </summary>
public static class ShellNavigation
{
    public static Task GoToNowPlayingAsync()
    {
        return GoToOnceAsync<NowPlayingPage>("nowplaying");
    }

    public static Task GoToQueueAsync()
    {
        return GoToOnceAsync<QueuePage>("queue");
    }

    private static Task GoToOnceAsync<TPage>(string route)
        where TPage : Page
    {
        if (Shell.Current is null)
        {
            return Task.CompletedTask;
        }

        var navigation = Shell.Current.Navigation;

        var alreadyOpen =
            (navigation.ModalStack?.Any(IsTarget) ?? false) ||
            (navigation.NavigationStack?.Any(IsTarget) ?? false);

        return alreadyOpen ? Task.CompletedTask : Shell.Current.GoToAsync(route);

        static bool IsTarget(Page? page)
        {
            return page is TPage || page is NavigationPage { RootPage: TPage };
        }
    }
}
