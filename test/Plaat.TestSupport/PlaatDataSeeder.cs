using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Plaat.Db;
using Plaat.Db.Models;

namespace Plaat.TestSupport;

/// <summary>
/// Seeds a Plaat SQLite database file directly through EF Core, the same way
/// Plaat.Db.Tests/Helpers/DbTestFixture does for the in-memory test database.
/// <para>
/// Crucially, all seeding for a given <see cref="PlaatApiTestServer"/> happens <b>before</b> the
/// real Plaat.Api process is started (see <see cref="PlaatApiTestServer.StartAsync"/>). Several
/// repositories (albums/artists/tracks/stats) sit behind an in-process, cache-aside
/// <c>ILibraryCache</c> inside the API process that is populated lazily on first read and never
/// invalidated by writes made from outside that process. Seeding first means the API's cache is
/// still cold when it starts, so its first real read naturally picks up everything seeded here.
/// Anything created *during* a test (e.g. a playlist through the UI) goes through the running API's
/// own repositories instead, which do keep the cache correct for their own writes.
/// </para>
/// </summary>
public sealed class PlaatDataSeeder(string connectionString)
{
    private DbContextOptions<PlaatContext> Options { get; } = new DbContextOptionsBuilder<PlaatContext>()
        .UseSqlite(connectionString)
        .Options;

    private PlaatContext CreateContext()
    {
        return new(Options);
    }

    /// <summary>
    /// Marks entities loaded (and saved) in an earlier <see cref="CreateContext"/> call as Unchanged
    /// in this new one, so setting them as a navigation property below doesn't make EF Core try to
    /// re-INSERT an already-existing row (each seed method opens its own short-lived context, so
    /// every entity handed in from a previous seed call is "detached" as far as this context knows).
    /// </summary>
    private static void AttachExisting<TEntity>(PlaatContext context, IEnumerable<TEntity> entities)
        where TEntity : class
    {
        foreach (var entity in entities)
        {
            context.Attach(entity);
        }
    }

    public void MigrateSchema()
    {
        using var context = CreateContext();
        context.Database.Migrate();
    }

    public Artist SeedArtist(string name)
    {
        using var context = CreateContext();
        var artist = new Artist { Name = name, NormalizedName = name.ToLowerInvariant() };
        context.Artists.Add(artist);
        context.SaveChanges();
        return artist;
    }

    public Album SeedAlbum(string title, params Artist[] artists)
    {
        using var context = CreateContext();
        AttachExisting(context, artists);
        var album = new Album { Title = title, AlbumArtists = [.. artists] };
        context.Albums.Add(album);
        context.SaveChanges();
        return album;
    }

    public Genre SeedGenre(string name)
    {
        using var context = CreateContext();
        var genre = new Genre { Name = name };
        context.Genres.Add(genre);
        context.SaveChanges();
        return genre;
    }

    /// <summary>
    /// Seeds a track backed by a real, freshly-generated silent WAV file so streaming/playback works end-to-end.
    /// </summary>
    public Track SeedPlayableTrack(
        Album album,
        string title,
        string audioDirectory,
        uint trackNumber = 1,
        TimeSpan? duration = null,
        DateTime? addedDate = null,
        uint playCount = 0,
        uint year = 0,
        IReadOnlyList<Artist>? artists = null,
        IReadOnlyList<Genre>? genres = null)
    {
        var actualDuration = duration ?? TimeSpan.FromSeconds(2);
        var filePath = TestAudioFactory.CreateSilentWavFile(
            Path.Combine(audioDirectory, $"{Guid.NewGuid():N}.wav"),
            actualDuration);

        using var context = CreateContext();
        if (artists is not null)
        {
            AttachExisting(context, artists);
        }

        if (genres is not null)
        {
            AttachExisting(context, genres);
        }

        var track = new Track
        {
            Title = title,
            AlbumId = album.Id,
            TrackNumber = trackNumber,
            FileHash = Guid.NewGuid().ToString("N"),
            OriginalContentHash = Guid.NewGuid().ToString("N"),
            MimeType = "audio/wav",
            Format = "wav",
            TrackLocations = [new TrackLocation { Path = filePath, Default = true, IsSupportedFormat = true }],
            AddedDate = addedDate ?? DateTime.UtcNow,
            PlayCount = playCount,
            Duration = actualDuration,
            FileSizeBytes = new FileInfo(filePath).Length,
            Year = year,
            Artists = artists?.ToList() ?? [],
            Genres = genres?.ToList() ?? []
        };
        context.Tracks.Add(track);
        context.SaveChanges();
        return track;
    }

    public Play SeedPlay(Track track, DateTime dateTime, User? user = null)
    {
        using var context = CreateContext();
        var play = new Play { TrackId = track.Id, DateTime = dateTime, UserId = user?.Id };
        context.Plays.Add(play);
        context.SaveChanges();
        return play;
    }

    public TrackRating SeedRating(Track track, User user, int stars)
    {
        using var context = CreateContext();
        var rating = new TrackRating { TrackId = track.Id, UserId = user.Id, Stars = stars, UpdatedDate = DateTime.UtcNow };
        context.Add(rating);
        context.SaveChanges();
        return rating;
    }

    public Playlist SeedPlaylist(string name, User owner, params Track[] tracks)
    {
        using var context = CreateContext();
        var playlist = new Playlist
        {
            Name = name,
            UserId = owner.Id,
            PlaylistTracks = [.. tracks.Select((track, index) => new PlaylistTrack { TrackId = track.Id, Position = index })]
        };
        context.Playlists.Add(playlist);
        context.SaveChanges();
        return playlist;
    }

    public FavoriteAlbum SeedFavoriteAlbum(User user, Album album)
    {
        using var context = CreateContext();
        var favorite = new FavoriteAlbum { UserId = user.Id, AlbumId = album.Id, AddedDate = DateTime.UtcNow };
        context.Add(favorite);
        context.SaveChanges();
        return favorite;
    }

    public FavoriteArtist SeedFavoriteArtist(User user, Artist artist)
    {
        using var context = CreateContext();
        var favorite = new FavoriteArtist { UserId = user.Id, ArtistId = artist.Id, AddedDate = DateTime.UtcNow };
        context.Add(favorite);
        context.SaveChanges();
        return favorite;
    }

    public MonitoredFolder SeedMonitoredFolder(string path)
    {
        using var context = CreateContext();
        var folder = new MonitoredFolder { Path = path };
        context.MonitoredFolders.Add(folder);
        context.SaveChanges();
        return folder;
    }

    /// <summary>
    /// Seeds an extra (non-admin) user with a real password hash, for auth scenarios beyond the auto-seeded admin.
    /// </summary>
    public User SeedUser(string username, string password, bool isAdmin = false)
    {
        var hasher = new PasswordHasher<string>();

        using var context = CreateContext();
        var user = new User
        {
            Username = username,
            PasswordHash = hasher.HashPassword(username, password),
            IsAdmin = isAdmin
        };
        context.Users.Add(user);
        context.SaveChanges();
        return user;
    }

    /// <summary>
    /// Seeds the same "admin"/"Admin123!" admin user Plaat.Api's own startup would otherwise create
    /// automatically (<c>Plaat.Db.ServiceCollectionExtensions.EnsureDbCreated</c>). Seed it ourselves,
    /// before the API process starts, whenever other seed data (plays, ratings, playlists, favorites)
    /// needs to reference a user - the API's own auto-seed only runs once its process is up, which is
    /// too late, and it no-ops anyway once it sees a user already exists.
    /// </summary>
    public User SeedAdminUser()
    {
        return SeedUser("admin", "Admin123!", isAdmin: true);
    }

    /// <summary>
    /// Fetches a user seeded elsewhere - most commonly via <see cref="SeedAdminUser"/>.
    /// </summary>
    public User GetUserByUsername(string username)
    {
        using var context = CreateContext();
        return context.Users.Single(u => u.Username == username);
    }
}
