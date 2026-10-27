using Plaat.Db.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

public sealed class AlbumRepositoryTests
{
    [Fact]
    public async Task GetAlbums_WhenEmpty_ReturnsEmptyCollection()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateAlbumRepository();

        var result = await repo.GetAlbums(CancellationToken.None);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetAlbums_WhenAlbumsExist_ReturnsAll()
    {
        using var fixture = new DbTestFixture();
        fixture.SeedAlbum("Album One");
        fixture.SeedAlbum("Album Two");

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbums(CancellationToken.None);

        result.Count.ShouldBe(2);
        result.Select(a => a.Title).ShouldContain("Album One");
        result.Select(a => a.Title).ShouldContain("Album Two");
    }

    [Fact]
    public async Task GetAlbums_IncludesArtistNames()
    {
        using var fixture = new DbTestFixture();
        await using var ctx = fixture.CreateContext();
        var artist = new Artist { Name = "Test Artist", NormalizedName = "Test Artist" };
        var album = new Album { Title = "Artist Album", AlbumArtists = [artist] };
        ctx.Albums.Add(album);
        await ctx.SaveChangesAsync();

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbums(CancellationToken.None);

        result.ShouldHaveSingleItem();
        result.First().Artists.Select(a => a.Name).ShouldContain("Test Artist");
    }

    [Fact]
    public async Task GetAlbum_WhenAlbumExists_ReturnsAlbumDetails()
    {
        using var fixture = new DbTestFixture();
        await using var ctx = fixture.CreateContext();
        var album = new Album { Title = "Detail Album" };
        ctx.Albums.Add(album);
        await ctx.SaveChangesAsync();

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbum(album.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Title.ShouldBe("Detail Album");
        result.Id.ShouldBe(album.Id);
    }

    [Fact]
    public async Task GetAlbum_WhenNotFound_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateAlbumRepository();

        var result = await repo.GetAlbum(999, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetAlbum_IncludesTracks()
    {
        using var fixture = new DbTestFixture();
        await using var ctx = fixture.CreateContext();
        var album = new Album { Title = "Album With Tracks" };
        ctx.Albums.Add(album);
        await ctx.SaveChangesAsync();

        var track = new Track
        {
            Title = "Track One",
            Album = album,
            FileHash = "abc123",
            MimeType = "audio/mpeg",
            TrackLocations = [new TrackLocation { Path = "/music/track.mp3" }]
        };
        ctx.Tracks.Add(track);
        await ctx.SaveChangesAsync();

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbum(album.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.ShouldHaveSingleItem();
        result.Tracks.First().Title.ShouldBe("Track One");
    }

    [Fact]
    public async Task GetAlbum_WhenAlbumHasDuplicateTracks_KeepsTheOneWithHigherPlayCount()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Doubled Album");

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Tracks.AddRange(
                new Track
                {
                    Title = "Song",
                    AlbumId = album.Id,
                    TrackNumber = 1,
                    PlayCount = 2,
                    BitRate = 128_000,
                    FileHash = "a",
                    MimeType = "audio/mpeg"
                },
                new Track
                {
                    Title = "Song",
                    AlbumId = album.Id,
                    TrackNumber = 1,
                    PlayCount = 9,
                    BitRate = 96_000,
                    FileHash = "b",
                    MimeType = "audio/mpeg"
                });
            await ctx.SaveChangesAsync();
        }

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbum(album.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.ShouldHaveSingleItem();
        result.Tracks.First().PlayCount.ShouldBe(9u);
    }

    [Fact]
    public async Task GetAlbum_WhenDuplicateTracksHaveZeroPlayCount_KeepsTheHigherBitrate()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Doubled Album");

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Tracks.AddRange(
                new Track
                {
                    Title = "Song",
                    AlbumId = album.Id,
                    TrackNumber = 1,
                    PlayCount = 0,
                    BitRate = 128_000,
                    FileHash = "a",
                    MimeType = "audio/mpeg"
                },
                new Track
                {
                    Title = "Song",
                    AlbumId = album.Id,
                    TrackNumber = 1,
                    PlayCount = 0,
                    BitRate = 320_000,
                    FileHash = "b",
                    MimeType = "audio/mpeg"
                });
            await ctx.SaveChangesAsync();
        }

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbum(album.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.ShouldHaveSingleItem();
        result.Tracks.First().Id.ShouldBe(2u);
    }

    [Fact]
    public async Task GetAlbum_WhenTracksDifferOnTitle_KeepsBoth()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Distinct Tracks");

        await using (var ctx = fixture.CreateContext())
        {
            ctx.Tracks.AddRange(
                new Track { Title = "One", AlbumId = album.Id, TrackNumber = 1, FileHash = "a", MimeType = "audio/mpeg" },
                new Track { Title = "Two", AlbumId = album.Id, TrackNumber = 2, FileHash = "b", MimeType = "audio/mpeg" });
            await ctx.SaveChangesAsync();
        }

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetAlbum(album.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetCoverArt_WhenAlbumHasNoCoverArt_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("No Cover");

        var repo = fixture.CreateAlbumRepository();
        var result = await repo.GetCoverArt(album.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task SetCoverArt_WhenAlbumHasNoCoverArt_AddsIt()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Cover Me");

        var repo = fixture.CreateAlbumRepository();
        var updated = await repo.SetCoverArt(album.Id, "abc.png", "ABC", "image/png", CancellationToken.None);

        updated.ShouldBeTrue();

        var result = await repo.GetCoverArt(album.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Path.ShouldBe("abc.png");
        result.Hash.ShouldBe("ABC");
        result.MimeType.ShouldBe("image/png");
    }

    [Fact]
    public async Task SetCoverArt_WhenAlbumAlreadyHasCoverArt_ReplacesIt()
    {
        using var fixture = new DbTestFixture();
        var album = fixture.SeedAlbum("Replace Cover");

        var repo = fixture.CreateAlbumRepository();
        await repo.SetCoverArt(album.Id, "old.jpg", "OLD", "image/jpeg", CancellationToken.None);
        await repo.SetCoverArt(album.Id, "new.webp", "NEW", "image/webp", CancellationToken.None);

        var result = await repo.GetCoverArt(album.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Path.ShouldBe("new.webp");
        result.Hash.ShouldBe("NEW");
        result.MimeType.ShouldBe("image/webp");
    }

    [Fact]
    public async Task SetCoverArt_DoesNotAffectOtherAlbumsSharingTheSameCoverArt()
    {
        using var fixture = new DbTestFixture();
        await using var ctx = fixture.CreateContext();
        var shared = new CoverArt { Path = "shared.jpg", Hash = "SHARED", MimeType = "image/jpeg" };
        var albumA = new Album { Title = "Album A", CoverArt = shared };
        var albumB = new Album { Title = "Album B", CoverArt = shared };
        ctx.Albums.AddRange(albumA, albumB);
        await ctx.SaveChangesAsync();

        var repo = fixture.CreateAlbumRepository();
        await repo.SetCoverArt(albumA.Id, "a-only.png", "AONLY", "image/png", CancellationToken.None);

        var coverA = await repo.GetCoverArt(albumA.Id, CancellationToken.None);
        var coverB = await repo.GetCoverArt(albumB.Id, CancellationToken.None);

        coverA.ShouldNotBeNull();
        coverA.Path.ShouldBe("a-only.png");
        coverB.ShouldNotBeNull();
        coverB.Path.ShouldBe("shared.jpg");
    }

    [Fact]
    public async Task SetCoverArt_WhenAlbumDoesNotExist_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var repo = fixture.CreateAlbumRepository();

        var updated = await repo.SetCoverArt(999, "x.png", "X", "image/png", CancellationToken.None);

        updated.ShouldBeFalse();
    }
}
