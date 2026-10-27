using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Plaat.Db.Models;
using Plaat.Db.Repositories;

namespace Plaat.Db;

public static class ServiceCollectionExtensions
{
    public static void AddDbServices(
        this IServiceCollection services,
        IWebHostEnvironment environment,
        string connectionString)
    {
        services.AddTransient<ITrackRepository, TrackRepository>();
        services.AddTransient<IAlbumRepository, AlbumRepository>();
        services.AddTransient<IArtistRepository, ArtistRepository>();
        services.AddTransient<IPlaylistRepository, PlaylistRepository>();
        services.AddTransient<IUserRepository, UserRepository>();
        services.AddTransient<IMonitoredFolderRepository, MonitoredFolderRepository>();
        services.AddTransient<ILogEntryRepository, LogEntryRepository>();
        services.AddTransient<IStatsRepository, StatsRepository>();
        services.AddTransient<ITrackRatingRepository, TrackRatingRepository>();

        services.AddMemoryCache();
        services.AddSingleton<ILibraryCache, LibraryCache>();

        services.AddDbContextFactory<PlaatContext>(options =>
        {
            var builder = options.UseSqlite(connectionString);
            if (environment.IsDevelopment())
            {
                builder.EnableSensitiveDataLogging();
            }
        });
    }

    public static async Task EnsureDbCreated(this IServiceProvider services)
    {
        await using var context = await services
            .GetRequiredService<IDbContextFactory<PlaatContext>>()
            .CreateDbContextAsync();

        await context.Database.MigrateAsync();

        if (!await context.Users.AnyAsync())
        {
            var hasher = services.GetRequiredService<IPasswordHasher<string>>();

            var user = new User
            {
                Username = "admin",
                PasswordHash = hasher.HashPassword("admin", "Admin123!"),
                IsAdmin = true
            };

            context.Users.Add(user);
            await context.SaveChangesAsync();
        }
    }
}
