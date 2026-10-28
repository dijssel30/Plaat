using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Db.Repositories;

namespace Plaat.Api.Tests;

public class PlaatApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public IAlbumRepository AlbumRepository { get; } = Substitute.For<IAlbumRepository>();
    public IArtistRepository ArtistRepository { get; } = Substitute.For<IArtistRepository>();
    public ITrackRepository TrackRepository { get; } = Substitute.For<ITrackRepository>();
    public IPlaylistRepository PlaylistRepository { get; } = Substitute.For<IPlaylistRepository>();
    public IUserRepository UserRepository { get; } = Substitute.For<IUserRepository>();
    public IMonitoredFolderRepository MonitoredFolderRepository { get; } = Substitute.For<IMonitoredFolderRepository>();
    public ILogEntryRepository LogEntryRepository { get; } = Substitute.For<ILogEntryRepository>();
    public IStatsRepository StatsRepository { get; } = Substitute.For<IStatsRepository>();
    public ITrackRatingRepository TrackRatingRepository { get; } = Substitute.For<ITrackRatingRepository>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection([
                new KeyValuePair<string, string?>("ConnectionStrings:SqlLiteConnection", "DataSource=:memory:"),
                new KeyValuePair<string, string?>("Logging:LogLevel:Default", "Warning"),
            ]);
        });

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(AlbumRepository);
            services.AddSingleton(ArtistRepository);
            services.AddSingleton(TrackRepository);
            services.AddSingleton(PlaylistRepository);
            services.AddSingleton(UserRepository);
            services.AddSingleton(MonitoredFolderRepository);
            services.AddSingleton(LogEntryRepository);
            services.AddSingleton(StatsRepository);
            services.AddSingleton(TrackRatingRepository);

            services.RemoveAll<IHostedService>();

            // Override with deterministic options, independent of whatever the developer's local
            // (gitignored, machine-specific) appsettings.json/appsettings.Secrets.json happen to contain.
            services.AddSingleton(new PlaatOptions
            {
                ImagesStoragePath = "/images",
                TargetReconvertedPath = "/reconverted",
                JwtOptions = new JwtOptions { Key = "test-key" },
                DiscogsSettings = new DiscogsSettings { Enabled = true }
            });

            // Replace the Basic auth scheme with a test scheme that always authenticates as admin
            services.AddAuthentication("Test")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

            services.AddAuthorization(options =>
            {
                options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder("Test")
                    .RequireAuthenticatedUser()
                    .Build();
            });
        });
    }
}
