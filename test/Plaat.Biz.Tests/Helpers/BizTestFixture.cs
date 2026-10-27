using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plaat.Db;
using Plaat.Db.Models;

namespace Plaat.Biz.Tests.Helpers;

/// <summary>
/// Concrete factory backed by a shared-cache in-memory SQLite database (a separate connection per context, so
/// background workers and tests never share one connection across threads; _connection keeps the database alive).
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

internal sealed class BizTestFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<PlaatContext> _dbOptions;

    public IDbContextFactory<PlaatContext> DbFactory { get; }

    public BizTestFixture()
    {
        _connection = new SqliteConnection($"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        _connection.Open();

        _dbOptions = new DbContextOptionsBuilder<PlaatContext>()
            .UseSqlite(_connection.ConnectionString)
            .Options;

        using var ctx = new PlaatContext(_dbOptions);
        ctx.Database.EnsureCreated();

        DbFactory = new TestDbContextFactory(_dbOptions);
    }

    public PlaatContext CreateContext()
    {
        return new(_dbOptions);
    }

    public User SeedUser(string username, bool isAdmin = false)
    {
        using var ctx = CreateContext();
        var user = new User { Username = username, PasswordHash = "hash", IsAdmin = isAdmin };
        ctx.Users.Add(user);
        ctx.SaveChanges();
        return user;
    }

    public Track SeedTrackWithLocation(string path, string title = "Track", TimeSpan? duration = null)
    {
        using var ctx = CreateContext();
        var album = new Album { Title = "Album" };
        var track = new Track
        {
            Title = title,
            Album = album,
            Duration = duration ?? TimeSpan.Zero,
            FileHash = Guid.NewGuid().ToString("N"),
            MimeType = "audio/mpeg",
            TrackLocations = [new TrackLocation { Path = path, IsSupportedFormat = true }]
        };
        ctx.Tracks.Add(track);
        ctx.SaveChanges();
        return track;
    }

    public ConvertQueueItem SeedConvertQueueItem(string trackPath, string fileHash, bool working = false, int attempts = 0)
    {
        using var ctx = CreateContext();
        var album = new Album { Title = "Album" };
        var trackLocation = new TrackLocation { Path = trackPath, IsSupportedFormat = false };
        var track = new Track
        {
            Title = "Track",
            Album = album,
            FileHash = fileHash,
            MimeType = "audio/mpeg",
            TrackLocations = [trackLocation]
        };
        var item = new ConvertQueueItem { TrackLocation = trackLocation, Working = working, Attempts = attempts };
        ctx.Tracks.Add(track);
        ctx.ConvertQueueItems.Add(item);
        ctx.SaveChanges();
        return item;
    }

    public MonitoredFolder SeedMonitoredFolder(string path)
    {
        using var ctx = CreateContext();
        var folder = new MonitoredFolder { Path = path };
        ctx.MonitoredFolders.Add(folder);
        ctx.SaveChanges();
        return folder;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
