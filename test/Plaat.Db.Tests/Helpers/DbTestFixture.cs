using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Plaat.Db.Models;
using Plaat.Db.Repositories;

namespace Plaat.Db.Tests.Helpers;

/// <summary>
/// Concrete factory backed by a shared in-memory SQLite connection.
/// </summary>
internal sealed class TestDbContextFactory(DbContextOptions<PlaatContext> options)
    : IDbContextFactory<PlaatContext>
{
    public PlaatContext CreateDbContext()
    {
        return new(options);
    }

    public Task<PlaatContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new PlaatContext(options));
    }
}

internal sealed class DbTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PlaatContext> _dbOptions;
    private readonly MemoryCache _memoryCache;
    private readonly TestDbContextFactory _dbFactory;

    public ILibraryCache Cache { get; }

    public DbTestFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<PlaatContext>()
            .UseSqlite(_connection)
            .Options;

        using var ctx = new PlaatContext(_dbOptions);
        ctx.Database.EnsureCreated();

        _dbFactory = new TestDbContextFactory(_dbOptions);

        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        Cache = new LibraryCache(_memoryCache);
    }

    public PlaatContext CreateContext()
    {
        return new(_dbOptions);
    }

    public AlbumRepository CreateAlbumRepository()
    {
        return new(_dbFactory, Cache);
    }

    public ArtistRepository CreateArtistRepository()
    {
        return new(_dbFactory, Cache);
    }

    public TrackRepository CreateTrackRepository()
    {
        return new(_dbFactory, Cache);
    }

    public UserRepository CreateUserRepository()
    {
        return new(_dbFactory);
    }

    public PlaylistRepository CreatePlaylistRepository()
    {
        return new(_dbFactory, Cache);
    }

    public MonitoredFolderRepository CreateMonitoredFolderRepository()
    {
        return new(_dbFactory);
    }

    public LogEntryRepository CreateLogEntryRepository()
    {
        return new(_dbFactory);
    }

    public StatsRepository CreateStatsRepository()
    {
        return new(_dbFactory, Cache);
    }

    public TrackRatingRepository CreateTrackRatingRepository()
    {
        return new(_dbFactory, Cache);
    }

    public Album SeedAlbum(string title, params Artist[] artists)
    {
        using var ctx = CreateContext();
        var album = new Album { Title = title, AlbumArtists = [.. artists] };
        ctx.Albums.Add(album);
        ctx.SaveChanges();
        return album;
    }

    public Track SeedTrack(
        Album album,
        string title,
        string filePath = "/music/test.mp3",
        DateTime? addedDate = null,
        uint playCount = 0,
        TimeSpan? duration = null,
        int bitRate = 0,
        uint year = 0,
        string? artistName = null)
    {
        using var ctx = CreateContext();
        var track = new Track
        {
            Title = title,
            AlbumId = album.Id,
            FileHash = Guid.NewGuid().ToString("N"),
            MimeType = "audio/mpeg",
            TrackLocations = [new TrackLocation { Path = filePath }],
            AddedDate = addedDate ?? DateTime.UtcNow,
            PlayCount = playCount,
            Duration = duration ?? TimeSpan.Zero,
            BitRate = bitRate,
            Year = year,
            Artists = artistName is null ? [] : [new Artist { Name = artistName, NormalizedName = artistName.ToLowerInvariant() }]
        };
        ctx.Tracks.Add(track);
        ctx.SaveChanges();
        return track;
    }

    public TrackLocation SeedTrackLocation(Track track, string path, bool isDefault = false, bool isSupportedFormat = true)
    {
        using var ctx = CreateContext();
        var location = new TrackLocation
        {
            TrackId = track.Id,
            Path = path,
            Default = isDefault,
            IsSupportedFormat = isSupportedFormat
        };
        ctx.TrackLocations.Add(location);
        ctx.SaveChanges();
        return location;
    }

    public Play SeedPlay(Track track, DateTime dateTime, User? user = null)
    {
        using var ctx = CreateContext();
        var play = new Play
        {
            TrackId = track.Id,
            DateTime = dateTime,
            UserId = user?.Id
        };
        ctx.Plays.Add(play);
        ctx.SaveChanges();
        return play;
    }

    public TrackRating SeedRating(Track track, User user, int stars)
    {
        using var ctx = CreateContext();
        var rating = new TrackRating
        {
            TrackId = track.Id,
            UserId = user.Id,
            Stars = stars,
            UpdatedDate = DateTime.UtcNow
        };
        ctx.Add(rating);
        ctx.SaveChanges();
        return rating;
    }

    public Artist SeedArtist(string name)
    {
        using var ctx = CreateContext();
        var artist = new Artist { Name = name, NormalizedName = name.ToLowerInvariant() };
        ctx.Artists.Add(artist);
        ctx.SaveChanges();
        return artist;
    }

    public Genre SeedGenre(string name)
    {
        using var ctx = CreateContext();
        var genre = new Genre { Name = name };
        ctx.Genres.Add(genre);
        ctx.SaveChanges();
        return genre;
    }

    public User SeedUser(string username, string passwordHash = "hash", bool isAdmin = false)
    {
        using var ctx = CreateContext();
        var user = new User { Username = username, PasswordHash = passwordHash, IsAdmin = isAdmin };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
        _connection.Dispose();
    }
}

