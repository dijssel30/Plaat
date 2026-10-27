using Microsoft.EntityFrameworkCore;
using Plaat.Db.Models;

namespace Plaat.Db;

public class PlaatContext(DbContextOptions<PlaatContext> options) : DbContext(options)
{
    public DbSet<Track> Tracks
    {
        get
        {
            return Set<Track>();
        }
    }

    public DbSet<Artist> Artists
    {
        get
        {
            return Set<Artist>();
        }
    }

    public DbSet<Album> Albums
    {
        get
        {
            return Set<Album>();
        }
    }

    public DbSet<CoverArt> CoverArts
    {
        get
        {
            return Set<CoverArt>();
        }
    }

    public DbSet<Playlist> Playlists
    {
        get
        {
            return Set<Playlist>();
        }
    }

    public DbSet<SmartPlaylist> SmartPlaylists
    {
        get
        {
            return Set<SmartPlaylist>();
        }
    }

    public DbSet<Genre> Genres
    {
        get
        {
            return Set<Genre>();
        }
    }

    public DbSet<TrackLocation> TrackLocations
    {
        get
        {
            return Set<TrackLocation>();
        }
    }

    public DbSet<User> Users
    {
        get
        {
            return Set<User>();
        }
    }

    public DbSet<RefreshToken> RefreshTokens
    {
        get
        {
            return Set<RefreshToken>();
        }
    }

    public DbSet<ConvertQueueItem> ConvertQueueItems
    {
        get
        {
            return Set<ConvertQueueItem>();
        }
    }

    public DbSet<Play> Plays
    {
        get
        {
            return Set<Play>();
        }
    }

    public DbSet<MonitoredFolder> MonitoredFolders
    {
        get
        {
            return Set<MonitoredFolder>();
        }
    }

    public DbSet<LogEntry> LogEntries
    {
        get
        {
            return Set<LogEntry>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlaatContext).Assembly);
    }
}
