using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Plaat.Mobile.Platforms.Android.Media;
using Plaat.Mobile.Platforms.Android.Navigation;
using Plaat.Mobile.Services.Navigation;

namespace Plaat.Mobile;

[Activity(Theme = "@style/Theme.Plaat", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.Portrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnPostCreate(Bundle? savedInstanceState)
    {
        base.OnPostCreate(savedInstanceState);

        if (Window?.DecorView is { } decorView)
        {
            TabReselectionHandler.Attach(decorView);
        }

        // Cold start (app wasn't running) from tapping the playback notification: Shell hasn't
        // finished its own async startup routing yet at this point, so this is only recorded here -
        // AppShell consumes it once "//home" actually becomes the current page.
        if (Intent?.GetBooleanExtra(QueueMediaSessionService.OpenNowPlayingExtra, false) == true)
        {
            ServiceHelper.GetRequiredService<AppLaunchIntentService>().RequestNowPlaying();
        }
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;

        // Warm start (app already running, LaunchMode.SingleTop reuses this activity): Shell is
        // already up, so navigate directly instead of going through AppLaunchIntentService.
        if (intent?.GetBooleanExtra(QueueMediaSessionService.OpenNowPlayingExtra, false) == true)
        {
            _ = ShellNavigation.GoToNowPlayingAsync();
        }
    }
}
