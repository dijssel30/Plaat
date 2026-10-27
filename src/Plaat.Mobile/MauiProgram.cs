using System.Net;
using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Contract.Models.Authorization;
using Plaat.Mobile.Services;
using Plaat.Mobile.Services.Auth;
using Plaat.Mobile.Services.Navigation;
using Plaat.Mobile.Services.Player;
using Plaat.Mobile.Services.Streaming;
using Plaat.Mobile.ViewModels;
using Plaat.Mobile.ViewModels.Player;
using Plaat.Mobile.Views;
using Plaat.Mobile.Views.TrackActions;

namespace Plaat.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            // false: CommunityToolkit's own Android foreground-service notification is disabled in favor
            // of QueueMediaSessionService (Platforms/Android/Media), which drives the real, queue-aware
            // session/notification (working Next/Previous) - see that file's comment for why.
            .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                // Aurora theme faces: DM Sans for UI text, Sora for titles.
                fonts.AddFont("DMSans-Regular.ttf", "DMSansRegular");
                fonts.AddFont("DMSans-Medium.ttf", "DMSansMedium");
                fonts.AddFont("DMSans-Bold.ttf", "DMSansBold");
                fonts.AddFont("Sora-SemiBold.ttf", "SoraSemiBold");
                fonts.AddFont("Sora-Bold.ttf", "SoraBold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Plaat.Contract's IPlaatClient/PlaatClient has no Blazor/ASP.NET Core dependency - it's
        // registered here exactly like Plaat.Frontend/Program.cs registers it, just with a MAUI-
        // specific ITokenSessionStore supplied below instead of Frontend's cookie/DataProtection one.
        // BaseUri starts from whatever was last saved on the login screen (see ServerAddressStore),
        // falling back to the build-time Constants.BackendBaseUri on first run.
        builder.Services.AddPlaatClient(
            new PlaatBackendOptions { BaseUri = ServerAddressStore.Load() },
            // Advertises Accept-Encoding so Plaat.Api compresses its JSON (the full album/artist
            // lists and track pages shrink several-fold), and transparently decompresses it again.
            client => client.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
            }));

        // Singleton, not Scoped: MAUI's generic host has one root service provider for the whole
        // app session (no per-circuit/per-request scope the way Blazor Server has), so this is the
        // one token holder for the app's single active user session.
        builder.Services.AddSingleton<TokenStorageService>();
        builder.Services.AddSingleton<AppSecureTokenSessionStore>();
        builder.Services.AddSingleton<ITokenSessionStore>(sp => sp.GetRequiredService<AppSecureTokenSessionStore>());
        builder.Services.AddSingleton<AccessTokenGate>();
        builder.Services.AddSingleton<AuthService>();
        builder.Services.AddSingleton<AppLaunchIntentService>();

        builder.Services.AddSingleton<LoopbackProxyService>();
        builder.Services.AddSingleton<QueueStateStore>();
        builder.Services.AddSingleton<PlayerQueueService>();
        builder.Services.AddSingleton<PlaybackEngine>();
        builder.Services.AddSingleton<PlaybackStateSyncService>();

        builder.Services.AddTransient<MiniPlayerBarViewModel>();

        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<HomeViewModel>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<TracksViewModel>();
        builder.Services.AddTransient<TracksPage>();
        builder.Services.AddTransient<OtherPage>();
        builder.Services.AddTransient<SearchViewModel>();
        builder.Services.AddTransient<SearchPage>();
        builder.Services.AddTransient<AlbumsViewModel>();
        builder.Services.AddTransient<AlbumsPage>();
        builder.Services.AddTransient<AlbumDetailViewModel>();
        builder.Services.AddTransient<AlbumDetailPage>();
        builder.Services.AddTransient<ArtistsViewModel>();
        builder.Services.AddTransient<ArtistsPage>();
        builder.Services.AddTransient<ArtistDetailViewModel>();
        builder.Services.AddTransient<ArtistDetailPage>();
        builder.Services.AddTransient<PlaylistsViewModel>();
        builder.Services.AddTransient<PlaylistsPage>();
        builder.Services.AddTransient<PlaylistDetailViewModel>();
        builder.Services.AddTransient<PlaylistDetailPage>();
        builder.Services.AddTransient<QueueViewModel>();
        builder.Services.AddTransient<QueuePage>();
        builder.Services.AddTransient<NowPlayingViewModel>();
        builder.Services.AddTransient<NowPlayingPage>();
        builder.Services.AddTransient<TrackDetailsViewModel>();
        builder.Services.AddTransient<TrackDetailsPage>();

        var app = builder.Build();

        ServiceHelper.Initialize(app.Services);
        app.Services.GetRequiredService<LoopbackProxyService>().Start();

        return app;
    }
}
