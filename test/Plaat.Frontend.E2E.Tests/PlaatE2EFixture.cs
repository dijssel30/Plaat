using Microsoft.Playwright;
using Plaat.TestSupport;
using Xunit;

namespace Plaat.Frontend.E2E.Tests;

/// <summary>
/// One real Plaat.Api + one real Plaat.Frontend (each an isolated child process - see
/// <see cref="PlaatFrontendTestServer"/> for why Frontend is a real process too, not just Api) + one
/// shared Chromium instance, for the whole assembly. All catalog data is seeded directly into the
/// database before the API process starts
/// (see <see cref="PlaatDataSeeder"/>'s doc comment for why that order matters), so it is already
/// present by the time any test's first request reaches the API.
/// <para>
/// Tests only ever read this seeded catalog or create their own uniquely-named playlists/ratings/etc.
/// through the UI, so sharing one instance across the whole assembly is safe and avoids paying
/// Plaat.Api's ~1-2s self-contained process startup (plus a one-time `dotnet publish`) per test class.
/// </para>
/// </summary>
public sealed class PlaatE2EFixture : IAsyncLifetime
{
    private static readonly Lazy<Task> BrowsersInstalled = new(InstallBrowsersAsync);

    public PlaatApiTestServer Api { get; } = new();
    public PlaatFrontendTestServer Frontend { get; private set; } = null!;
    public SeededCatalog Catalog { get; private set; } = null!;

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;

    public async Task InitializeAsync()
    {
        await Api.PrepareAsync();
        Catalog = SeedCatalog(Api.Seeder, Api.AudioDirectory);
        await Api.StartAsync();

        Frontend = new PlaatFrontendTestServer { BackendBaseUri = Api.BaseUri };
        await Frontend.StartAsync();

        await BrowsersInstalled.Value;
        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = Environment.GetEnvironmentVariable("PLAAT_E2E_HEADED") != "1"
        });
    }

    public async Task DisposeAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
        await Frontend.DisposeAsync();
        await Api.DisposeAsync();
    }

    /// <summary>
    /// A fresh, isolated browser context+page (its own cookies/local storage) pointed at the app root.
    /// </summary>
    public async Task<IPage> NewPageAsync()
    {
        var context = await _browser.NewContextAsync(new BrowserNewContextOptions { BaseURL = Frontend.BaseUri.ToString() });
        var page = await context.NewPageAsync();

        page.Console += (_, msg) => Console.WriteLine($"[browser console:{msg.Type}] {msg.Text}");
        page.PageError += (_, err) => Console.WriteLine($"[browser page error] {err}");
        page.RequestFailed += (_, req) => Console.WriteLine($"[browser request failed] {req.Method} {req.Url} - {req.Failure}");
        page.Response += (_, resp) =>
        {
            if (resp.Status >= 400)
            {
                Console.WriteLine($"[browser response {resp.Status}] {resp.Url}");
            }
        };

        // Tests do `await using var page = await fixture.NewPageAsync();`, which only closes the
        // page itself - Playwright does not auto-close a context when its last page closes. Left
        // unclosed, every test's context (each holding its own SignalR/circuit connection) piled up
        // for the lifetime of the one shared Chromium instance, and later tests in the run started
        // timing out once enough of them had accumulated. Closing the context when its page closes
        // keeps `await using var page = ...` working exactly as tests already expect.
        page.Close += async (_, _) =>
        {
            try
            {
                await context.CloseAsync();
            }
            catch
            {
                // best-effort cleanup
            }
        };

        return page;
    }

    /// <summary>
    /// A fresh page, already logged in through the real login form.
    /// </summary>
    public async Task<IPage> NewAuthenticatedPageAsync(string username = "admin", string password = "Admin123!")
    {
        var page = await NewPageAsync();
        await page.GotoAsync("/login");
        await page.FillAsync("#username", username);
        await page.FillAsync("#password", password);
        await page.ClickAsync(".login-btn");
        await page.WaitForURLAsync(url => !url.Contains("/login"));
        return page;
    }

    private static async Task InstallBrowsersAsync()
    {
        // Idempotent - a no-op if Chromium is already installed - so this stays cheap on repeat runs.
        var exitCode = Program.Main(["install", "chromium"]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"'playwright install chromium' exited with code {exitCode}. If this is a fresh machine, " +
                "system libraries Chromium needs may be missing - try running " +
                "'pwsh bin/Debug/net10.0/playwright.ps1 install --with-deps chromium' " +
                "(or the equivalent apt packages) once manually.");
        }
    }

    private static SeededCatalog SeedCatalog(PlaatDataSeeder seeder, string audioDirectory)
    {
        var admin = seeder.SeedAdminUser();
        var secondaryUser = seeder.SeedUser("secondary", SeededCatalog.SecondaryUserPassword);
        var passwordChangeUser = seeder.SeedUser("password-change-user", SeededCatalog.PasswordChangeUserPassword);

        var rock = seeder.SeedGenre("Rock");
        var jazz = seeder.SeedGenre("Jazz");

        var artistOne = seeder.SeedArtist("Test Artist One");
        var artistTwo = seeder.SeedArtist("Test Artist Two");

        var albumOne = seeder.SeedAlbum("Test Album One", artistOne);
        var albumTwo = seeder.SeedAlbum("Test Album Two", artistTwo);

        var trackOne = seeder.SeedPlayableTrack(
            albumOne, "Opening Track", audioDirectory,
            trackNumber: 1, duration: TimeSpan.FromSeconds(3),
            addedDate: DateTime.UtcNow.AddDays(-1),
            artists: [artistOne], genres: [rock]);

        var trackTwo = seeder.SeedPlayableTrack(
            albumOne, "Second Track", audioDirectory,
            trackNumber: 2, duration: TimeSpan.FromSeconds(4),
            addedDate: DateTime.UtcNow.AddDays(-1),
            playCount: 3,
            artists: [artistOne], genres: [rock]);

        var trackThree = seeder.SeedPlayableTrack(
            albumTwo, "Only Track", audioDirectory,
            trackNumber: 1, duration: TimeSpan.FromSeconds(5),
            addedDate: DateTime.UtcNow,
            artists: [artistTwo], genres: [jazz]);

        seeder.SeedPlay(trackTwo, DateTime.UtcNow.AddHours(-1), admin);
        seeder.SeedPlay(trackTwo, DateTime.UtcNow.AddDays(-2), admin);
        seeder.SeedPlay(trackThree, DateTime.UtcNow.AddMinutes(-10), admin);

        seeder.SeedRating(trackOne, admin, stars: 4);

        var playlist = seeder.SeedPlaylist("Existing Playlist", admin, trackOne, trackTwo);

        seeder.SeedFavoriteAlbum(admin, albumOne);
        seeder.SeedFavoriteArtist(admin, artistTwo);

        var monitoredFolder = seeder.SeedMonitoredFolder("/music/monitored-fixture-folder");

        return new SeededCatalog
        {
            Admin = admin,
            SecondaryUser = secondaryUser,
            PasswordChangeUser = passwordChangeUser,
            ArtistOne = artistOne,
            ArtistTwo = artistTwo,
            AlbumOne = albumOne,
            AlbumTwo = albumTwo,
            TrackOne = trackOne,
            TrackTwo = trackTwo,
            TrackThree = trackThree,
            Playlist = playlist,
            MonitoredFolder = monitoredFolder
        };
    }
}

[CollectionDefinition(Name)]
public sealed class PlaatE2ECollection : ICollectionFixture<PlaatE2EFixture>
{
    public const string Name = "Plaat.E2E";
}
