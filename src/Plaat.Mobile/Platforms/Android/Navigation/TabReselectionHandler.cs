using Android.Views;
using Google.Android.Material.Navigation;

namespace Plaat.Mobile.Platforms.Android.Navigation;

/// <summary>
/// MAUI Shell raises no event when the user taps the already-active bottom TabBar item - the
/// native BottomNavigationView swallows it entirely (a known Shell limitation, dotnet/maui#9256).
/// This hooks the native widget directly (found by walking the Activity's decor view tree, since
/// Shell's Android renderer doesn't expose it through any public MAUI API) so re-tapping a tab
/// resets that tab's own navigation stack back to its root page, matching the platform convention
/// used by apps like Deezer/Spotify.
/// </summary>
public static class TabReselectionHandler
{
    private static readonly Dictionary<string, string> RouteByTitle = new()
    {
        ["Home"] = "home",
        ["Albums"] = "albums",
        ["Artists"] = "artists",
        ["Playlists"] = "playlists",
        ["Other"] = "other"
    };

    private static readonly ReselectListener Listener = new();

    public static void Attach(global::Android.Views.View rootView)
    {
        Poll(rootView, elapsedMs: 0);
    }

    /// <summary>
    /// Re-searches the tree and re-sets the listener every 500ms for 15s after the activity is
    /// created. A single early attach isn't enough - Shell finishes wiring up its own selection
    /// listeners on the same widget shortly after this point in the lifecycle, silently replacing
    /// (rather than chaining with) whatever was set before it. Re-searching from scratch each time
    /// (instead of reusing one captured reference) also covers Shell swapping out the widget
    /// instance itself; polling stops after 15s, comfortably past Shell's startup window, so this
    /// doesn't run forever (an earlier version that reasserted forever crashed with
    /// ObjectDisposedException once the widget it had captured was torn down).
    /// </summary>
    private static void Poll(global::Android.Views.View rootView, int elapsedMs)
    {
        FindBottomNavigationView(rootView)?.SetOnItemReselectedListener(Listener);

        if (elapsedMs < 15000)
        {
            rootView.PostDelayed(() => Poll(rootView, elapsedMs + 500), 500);
        }
    }

    private static NavigationBarView? FindBottomNavigationView(global::Android.Views.View view)
    {
        if (view is NavigationBarView navBar)
        {
            return navBar;
        }

        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is not null && FindBottomNavigationView(child) is { } found)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private sealed class ReselectListener : Java.Lang.Object, NavigationBarView.IOnItemReselectedListener
    {
        public void OnNavigationItemReselected(IMenuItem item)
        {
            var title = item.TitleFormatted?.ToString();
            if (title is not null && RouteByTitle.TryGetValue(title, out var route))
            {
                _ = Shell.Current.GoToAsync($"//{route}");
            }
        }
    }
}
