using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class ServiceCollectionExtensionsTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"plaat-di-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private ServiceProvider BuildProvider(string environmentName)
    {
        var environment = Substitute.For<IWebHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var services = new ServiceCollection();
        services.AddSingleton<IPasswordHasher<string>, PasswordHasher<string>>();
        services.AddDbServices(environment, $"Data Source={_dbPath}");

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void AddDbServices_RegistersRepositoriesCacheAndContextFactory(string environmentName)
    {
        using var provider = BuildProvider(environmentName);

        provider.GetRequiredService<ITrackRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IAlbumRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IArtistRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IPlaylistRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IUserRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IMonitoredFolderRepository>().ShouldNotBeNull();
        provider.GetRequiredService<ILogEntryRepository>().ShouldNotBeNull();
        provider.GetRequiredService<IStatsRepository>().ShouldNotBeNull();
        provider.GetRequiredService<ITrackRatingRepository>().ShouldNotBeNull();
        provider.GetRequiredService<ILibraryCache>().ShouldBeSameAs(provider.GetRequiredService<ILibraryCache>());

        using var context = provider.GetRequiredService<IDbContextFactory<PlaatContext>>().CreateDbContext();
        context.Database.GetDbConnection().DataSource.ShouldBe(_dbPath);
    }

    [Fact]
    public async Task EnsureDbCreated_MigratesAndSeedsASingleAdminOnce()
    {
        await using var provider = BuildProvider("Production");

        await provider.EnsureDbCreated();
        await provider.EnsureDbCreated();

        await using var context = await provider.GetRequiredService<IDbContextFactory<PlaatContext>>().CreateDbContextAsync();
        var admin = await context.Users.SingleAsync();
        admin.Username.ShouldBe("admin");
        admin.IsAdmin.ShouldBeTrue();
        new PasswordHasher<string>().VerifyHashedPassword("admin", admin.PasswordHash, "Admin123!")
            .ShouldNotBe(PasswordVerificationResult.Failed);
        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }
}
