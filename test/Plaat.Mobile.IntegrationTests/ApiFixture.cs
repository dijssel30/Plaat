using Plaat.TestSupport;
using Xunit;

namespace Plaat.Mobile.IntegrationTests;

/// <summary>
/// One real, isolated Plaat.Api process shared by every test in this assembly (see
/// <see cref="ApiCollection"/>). These tests only probe network/auth behavior against endpoints
/// that don't collide between tests (login, streaming a specific track, proxying cover art), so a
/// single shared instance is safe and avoids paying Plaat.Api's ~1-2s self-contained process
/// startup cost per test class.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public PlaatApiTestServer Server { get; } = new();

    public uint SeededTrackId { get; private set; }
    public uint SeededAlbumId { get; private set; }
    public uint SeededArtistId { get; private set; }

    public async Task InitializeAsync()
    {
        await Server.PrepareAsync();

        var artist = Server.Seeder.SeedArtist("Fixture Artist");
        var album = Server.Seeder.SeedAlbum("Fixture Album", artist);
        var track = Server.Seeder.SeedPlayableTrack(
            album,
            "Fixture Track",
            Server.AudioDirectory,
            duration: TimeSpan.FromSeconds(3),
            artists: [artist]);

        SeededArtistId = artist.Id;
        SeededAlbumId = album.Id;
        SeededTrackId = track.Id;

        await Server.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Plaat.Api";
}
