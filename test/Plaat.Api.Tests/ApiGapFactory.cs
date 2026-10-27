using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Biz.Scanners;
using Plaat.Db;
using Plaat.Db.Models;

namespace Plaat.Api.Tests;

/// <summary>
/// <see cref="PlaatApiWebApplicationFactory"/> plus what the auth, scan/import and file-serving
/// endpoints need beyond substituted repositories: a real (temp, file-backed) SQLite database with
/// seeded users for <c>TokenService</c>, substituted scanners so background scans never touch disk,
/// a temp images folder, and a signing key long enough for HS256.
/// </summary>
public sealed class ApiGapFactory : PlaatApiWebApplicationFactory
{
    public const string Password = "correct horse battery";

    private readonly string _root = Directory.CreateTempSubdirectory("plaat-api-").FullName;

    public IMusicScanner MusicScanner { get; } = Substitute.For<IMusicScanner>();

    public IItunesLibraryImporter ItunesImporter { get; } = Substitute.For<IItunesLibraryImporter>();

    public string ImagesPath
    {
        get
        {
            return Path.Combine(_root, "images");
        }
    }

    public string WorkPath
    {
        get
        {
            return _root;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        var dbOptions = new DbContextOptionsBuilder<PlaatContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "api.db")};Pooling=False")
            .Options;

        using (var context = new PlaatContext(dbOptions))
        {
            context.Database.EnsureCreated();
            var hasher = new PasswordHasher<string>();
            context.Users.AddRange(
                new User { Username = "testuser", PasswordHash = hasher.HashPassword("testuser", Password), IsAdmin = true },
                new User { Username = "alice", PasswordHash = hasher.HashPassword("alice", Password) });
            context.SaveChanges();
        }

        Directory.CreateDirectory(ImagesPath);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDbContextFactory<PlaatContext>>(new FixedContextFactory(dbOptions));
            services.AddTransient(_ => MusicScanner);
            services.AddTransient(_ => ItunesImporter);
            services.AddSingleton(new PlaatOptions
            {
                ImagesStoragePath = ImagesPath,
                TargetReconvertedPath = Path.Combine(_root, "reconverted"),
                JwtOptions = new JwtOptions { Key = "a-signing-key-that-is-long-enough-for-hs256" },
                DiscogsSettings = new DiscogsSettings { Enabled = true },
                RateLimitSettings = new RateLimitSettings { Enabled = false }
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class FixedContextFactory(DbContextOptions<PlaatContext> options) : IDbContextFactory<PlaatContext>
    {
        public PlaatContext CreateDbContext()
        {
            return new PlaatContext(options);
        }
    }
}
