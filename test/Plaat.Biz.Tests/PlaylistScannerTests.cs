using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Db.Repositories;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class PlaylistScannerTests
{
    private static PlaatOptions BuildOptions()
    {
        return new PlaatOptions
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/converted",
            JwtOptions = new JwtOptions { Key = "test-key" }
        };
    }

    private static string WriteM3u(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public async Task ImportM3uPlaylist_AllPathsExistInDb_CreatesPlaylistWithAllTracks()
    {
        using var fixture = new BizTestFixture();
        var trackA = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        var trackB = fixture.SeedTrackWithLocation("/music/b.mp3", "B");

        var m3uPath = WriteM3u("/music/a.mp3", "/music/b.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var replacer = new FilePathReplacer(BuildOptions());
        var logger = Substitute.For<ILogger<PlaylistScanner>>();
        var scanner = new PlaylistScanner(repository, fixture.DbFactory, replacer, logger);

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            await repository.Received(1).CreatePlaylist(
                Path.GetFileNameWithoutExtension(m3uPath),
                1,
                Arg.Is<IReadOnlyCollection<uint>>(t => t!.Count == 2 && t.Contains(trackA.Id) && t.Contains(trackB.Id)),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }

    [Fact]
    public async Task ImportM3uPlaylist_PathNotInDb_SkipsMissingEntryAndKeepsFound()
    {
        using var fixture = new BizTestFixture();
        var trackA = fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var m3uPath = WriteM3u("/music/a.mp3", "/music/missing.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var replacer = new FilePathReplacer(BuildOptions());
        var logger = Substitute.For<ILogger<PlaylistScanner>>();
        var scanner = new PlaylistScanner(repository, fixture.DbFactory, replacer, logger);

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            await repository.Received(1).CreatePlaylist(
                Arg.Any<string>(),
                1,
                Arg.Is<IReadOnlyCollection<uint>>(t => t!.Count == 1 && t.Contains(trackA.Id)),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }

    [Fact]
    public async Task ImportM3uPlaylist_SkipsExtendedM3uDirectivesAndBlankLines()
    {
        using var fixture = new BizTestFixture();
        var trackA = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        var trackB = fixture.SeedTrackWithLocation("/music/b.mp3", "B");

        var m3uPath = WriteM3u(
            "#EXTM3U",
            "#EXTINF:123,Artist - A",
            "/music/a.mp3",
            "",
            "#EXTINF:456,Artist - B",
            "/music/b.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var replacer = new FilePathReplacer(BuildOptions());
        var logger = Substitute.For<ILogger<PlaylistScanner>>();
        var scanner = new PlaylistScanner(repository, fixture.DbFactory, replacer, logger);

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            await repository.Received(1).CreatePlaylist(
                Arg.Any<string>(),
                1,
                Arg.Is<IReadOnlyCollection<uint>>(t => t!.Count == 2 && t.Contains(trackA.Id) && t.Contains(trackB.Id)),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }

    [Fact]
    public async Task ImportM3uPlaylist_NoPathsFound_DoesNotCreatePlaylist()
    {
        using var fixture = new BizTestFixture();

        var m3uPath = WriteM3u("/music/missing.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var replacer = new FilePathReplacer(BuildOptions());
        var logger = Substitute.For<ILogger<PlaylistScanner>>();
        var scanner = new PlaylistScanner(repository, fixture.DbFactory, replacer, logger);

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            await repository.DidNotReceive().CreatePlaylist(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }

    [Fact]
    public async Task ImportM3uPlaylist_UsesFileNameWithoutExtensionAsPlaylistName()
    {
        using var fixture = new BizTestFixture();
        fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var m3uPath = WriteM3u("/music/a.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var replacer = new FilePathReplacer(BuildOptions());
        var logger = Substitute.For<ILogger<PlaylistScanner>>();
        var scanner = new PlaylistScanner(repository, fixture.DbFactory, replacer, logger);

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            var expectedName = Path.GetFileNameWithoutExtension(m3uPath);
            await repository.Received(1).CreatePlaylist(
                expectedName, Arg.Any<int>(), Arg.Any<IReadOnlyCollection<uint>>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }

    [Fact]
    public async Task ImportM3uPlaylist_PassesTracksInFileOrder()
    {
        using var fixture = new BizTestFixture();
        var trackA = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        var trackB = fixture.SeedTrackWithLocation("/music/b.mp3", "B");

        var m3uPath = WriteM3u("/music/b.mp3", "/music/a.mp3");
        var repository = Substitute.For<IPlaylistRepository>();
        var scanner = new PlaylistScanner(
            repository, fixture.DbFactory, new FilePathReplacer(BuildOptions()), Substitute.For<ILogger<PlaylistScanner>>());

        try
        {
            await scanner.ImportM3uPlaylist(m3uPath, userId: 1, CancellationToken.None);

            await repository.Received(1).CreatePlaylist(
                Arg.Any<string>(),
                1,
                Arg.Is<IReadOnlyCollection<uint>>(t => t!.SequenceEqual(new[] { trackB.Id, trackA.Id })),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(m3uPath);
        }
    }
}
