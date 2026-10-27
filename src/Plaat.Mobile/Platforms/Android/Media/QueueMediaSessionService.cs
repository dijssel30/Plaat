using System.ComponentModel;
using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Media3.Session;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;

namespace Plaat.Mobile.Platforms.Android.Media;

/// <summary>
/// Owns the app's real, system-visible media session/notification - see QueueSessionPlayer for why
/// this exists instead of relying on CommunityToolkit.Maui.MediaElement's own auto-generated one.
/// CommunityToolkit's own foreground service is turned off (MauiProgram's
/// UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)) so there's only ever
/// one media notification; this service being foreground still protects the whole app process (and
/// therefore CommunityToolkit's private ExoPlayer inside it, which is what actually plays the audio)
/// from being killed in the background, exactly like CommunityToolkit's own service used to.
/// </summary>
[Service(Exported = false, Enabled = true, ForegroundServiceType = ForegroundService.TypeMediaPlayback)]
[IntentFilter(["androidx.media3.session.MediaSessionService"])]
public sealed class QueueMediaSessionService : MediaSessionService
{
    private const int NotificationId = 1;
    private const string ChannelId = "plaat_playback";

    /// <summary>
    /// Intent extra used to tell MainActivity to open the full-screen player when launched
    /// by tapping this notification - see MainActivity's OnPostCreate/OnNewIntent.
    /// </summary>
    public const string OpenNowPlayingExtra = "plaat.open_now_playing";

    private static bool _started;

    private readonly HttpClient _artworkClient = new();

    private MediaSession? _session;
    private QueueSessionPlayer? _player;
    private PlayerQueueService? _queue;
    private LoopbackProxyService? _proxy;
    private NotificationManager? _notificationManager;
    private NotificationCompat.Builder? _notificationBuilder;
    private Timer? _positionTimer;
    private uint? _artworkTrackId;

    // The track the notification was last built for (null for "nothing playing") - see OnQueueChanged.
    private uint? _notifiedTrackId;
    private bool _hasNotified;

    /// <summary>
    /// Starts the service once, the first time the queue actually begins playing (mirrors the
    /// "notification only appears once something plays" behavior of real music apps).
    /// </summary>
    public static void EnsureStarted(Context context)
    {
        if (_started)
        {
            return;
        }

        _started = true;

        var intent = new Intent(context, typeof(QueueMediaSessionService));
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public override void OnCreate()
    {
        base.OnCreate();

        _queue = ServiceHelper.GetRequiredService<PlayerQueueService>();
        var engine = ServiceHelper.GetRequiredService<PlaybackEngine>();
        _proxy = ServiceHelper.GetRequiredService<LoopbackProxyService>();

        _player = new QueueSessionPlayer(Looper.MainLooper!, _queue, engine, _proxy);

        // Tapping the notification (or its MediaStyle content area) should open the full-screen
        // player, not whatever Android's default "resume the task" behavior would do - MainActivity
        // reads this extra (OnPostCreate for a cold start, OnNewIntent for a warm one) and navigates
        // to the "nowplaying" Shell route.
        var activityIntent = new Intent(this, typeof(MainActivity));
        activityIntent.PutExtra(OpenNowPlayingExtra, true);
        var contentIntent = PendingIntent.GetActivity(this, 0, activityIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;

        // The binding's Builder-pattern methods are conservatively typed as returning nullable
        // references even though these Java "return this;" fluent APIs never actually return null -
        // the null-forgiving operators below suppress that false-positive nullable-flow noise.
        _session = new MediaSession.Builder(this, _player).SetId("plaat_queue")!.SetSessionActivity(contentIntent)!.Build();
        AddSession(_session);

        _notificationManager = (NotificationManager)GetSystemService(NotificationService)!;
        _notificationManager.CreateNotificationChannel(new NotificationChannel(ChannelId, "Playback", NotificationImportance.Low));
        _notificationBuilder = new NotificationCompat.Builder(this, ChannelId)
            .SetSmallIcon(ResourceConstant.Drawable.media3_notification_small_icon)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .SetContentIntent(contentIntent)!
            .SetStyle(new MediaStyleNotificationHelper.MediaStyle(_session));

        // Make sure the player's State (and therefore the session's reported MediaMetadata) reflects
        // the current track before the first notification is built, rather than SimpleBasePlayer's
        // still-default placeholder state.
        _player.RefreshPosition();

        // Context.StartForegroundService() (called from EnsureStarted) requires the service to call
        // Service.StartForeground() almost immediately, or the OS kills the whole app process with
        // ForegroundServiceDidNotStartInTimeException. MediaSessionService's own default notification
        // pipeline does this automatically eventually, but not reliably within that window here (it
        // waits on the session's playback state to settle, artwork to resolve, etc.) - building and
        // posting a first notification ourselves, synchronously, right here is what actually satisfies
        // the deadline. Every subsequent PostNotification call just refreshes the same notification.
        PostNotification(startForeground: true);

        _queue.PlaylistChanged += OnQueueChanged;
        _queue.PropertyChanged += OnQueuePropertyChanged;

        // The session's PlaybackState timestamp lets the OS extrapolate position between updates, but
        // a periodic nudge keeps the notification's scrubber from visibly drifting from the real
        // position over a long-playing track. This only needs to refresh the player's own State (which
        // is what the system reads for the scrubber) - not rebuild/re-Notify() the notification itself,
        // which only needs to happen when title/artist/artwork/actions actually change.
        _positionTimer = new Timer(_ => _player?.RefreshPosition(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// PlayerQueueService raises a burst of events for every change - a track change alone is four
    /// PropertyChanged plus PlaylistChanged - and each used to rebuild and re-post the whole
    /// notification, several times per skip, which Android starts throttling at that rate anyway. The
    /// notification itself only carries the track's title/artist/artwork (play state and position come
    /// from the session), so it only needs rebuilding when the track actually changes.
    /// </summary>
    private void OnQueueChanged()
    {
        if (_hasNotified && _queue?.CurrentValue?.Id == _notifiedTrackId)
        {
            return;
        }

        PostNotification(startForeground: false);
    }

    private void OnQueuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnQueueChanged();
    }

    /// <summary>
    /// The base <see cref="MediaSessionService"/> posts its own notification (Media3's default
    /// provider - id 1001, channel "default_channel_id") whenever a session's player starts playing.
    /// We already build and post the real one ourselves (<see cref="PostNotification"/>, id
    /// <see cref="NotificationId"/>), so letting the base one through means two playback
    /// notifications for the same session. Route the base's update trigger to our single
    /// notification instead of calling <c>base</c>.
    /// </summary>
    public override void OnUpdateNotification(MediaSession? mediaSession, bool startInForegroundRequired)
    {
        PostNotification(startForeground: startInForegroundRequired);
    }

    private void PostNotification(bool startForeground)
    {
        // Captured into a local rather than dereferencing the field directly: nullable narrowing from
        // the guard below doesn't survive the _queue?.CurrentValue property read further down (any
        // call in between invalidates a field's narrowed null-state, since it could theoretically
        // reassign the field), so the field would otherwise still read as possibly-null past that point.
        var notificationBuilder = _notificationBuilder;
        if (notificationBuilder is null)
        {
            return;
        }

        // We build the notification ourselves with the low-level MediaStyleNotificationHelper.MediaStyle
        // (just the session token + transport-control layout) rather than going through Media3's own
        // MediaNotification.Provider/DefaultMediaNotificationProvider, so - unlike that higher-level
        // path - nothing here auto-populates contentTitle/contentText/largeIcon from the session; those
        // have to be set explicitly on the builder every time, same as any other Android notification.
        // The scrubber and session-driven parts of the layout (metadata, position) come from the
        // session/player's Player.Commands including the "Get" commands (see QueueSessionPlayer's
        // GetState) - without those, external observers (this notification, dumpsys, system UI) can't
        // read the player's state even though it's set correctly, so the scrubber never appeared.
        var current = _queue?.CurrentValue;
        _notifiedTrackId = current?.Id;
        _hasNotified = true;

        notificationBuilder
            .SetContentTitle(current?.Title ?? "Plaat")!
            .SetContentText(current?.AllArtists ?? string.Empty);

        if (current is not null && current.Id != _artworkTrackId)
        {
            LoadArtwork(current.Id);
        }

        var notification = notificationBuilder.Build()!;

        if (startForeground)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }
        }
        else
        {
            _notificationManager?.Notify(NotificationId, notification);
        }
    }

    private void LoadArtwork(uint trackId)
    {
        _artworkTrackId = trackId;
        _ = LoadArtworkAsync(trackId);
    }

    private async Task LoadArtworkAsync(uint trackId)
    {
        if (_proxy is null)
        {
            return;
        }

        try
        {
            var bytes = await _artworkClient.GetByteArrayAsync(_proxy.TrackCoverUrl(trackId));
            var bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);

            // The queue may have moved on again while the fetch was in flight - only apply this
            // artwork if it's still the track currently showing (same stale-load concern as
            // PlaybackEngine's _loadInFlight guard, just for this independent fetch).
            if (bitmap is null || _queue?.CurrentValue?.Id != trackId || _notificationBuilder is null)
            {
                return;
            }

            _notificationBuilder.SetLargeIcon(bitmap);
            PostNotification(startForeground: false);
        }
        catch (Exception)
        {
            // Best-effort - the notification just keeps showing without artwork.
        }
    }

    public override MediaSession? OnGetSession(MediaSession.ControllerInfo? controllerInfo)
    {
        return _session;
    }

    public override void OnDestroy()
    {
        if (_queue is not null)
        {
            _queue.PlaylistChanged -= OnQueueChanged;
            _queue.PropertyChanged -= OnQueuePropertyChanged;
        }

        _positionTimer?.Dispose();
        _positionTimer = null;

        _session?.Player?.Release();
        _session?.Release();
        _session = null;
        _player = null;
        _queue = null;
        _proxy = null;
        _artworkTrackId = null;
        _notifiedTrackId = null;
        _hasNotified = false;

        _started = false;

        base.OnDestroy();
    }
}
