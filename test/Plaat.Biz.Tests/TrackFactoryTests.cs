using System.Net.Mime;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Db.Models;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class TrackFactoryTests : IDisposable
{
    private readonly BizTestFixture _fixture = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("plaat-trackfactory-").FullName;
    private readonly string _imagesDir;
    private readonly TrackFactory _factory;

    public TrackFactoryTests()
    {
        _imagesDir = Directory.CreateDirectory(Path.Combine(_dir, "images")).FullName;
        var options = new PlaatOptions
        {
            ImagesStoragePath = _imagesDir,
            TargetReconvertedPath = Path.Combine(_dir, "converted"),
            JwtOptions = new JwtOptions { Key = "test-key" }
        };

        _factory = new TrackFactory(
            options,
            new MediaInfoParser(NullLogger<MediaInfoParser>.Instance),
            NullLogger<TrackFactory>.Instance);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private string AlbumDir(string name)
    {
        return Directory.CreateDirectory(Path.Combine(_dir, name)).FullName;
    }

    private async Task<Track> Build(string path, TrackInfo? info = null)
    {
        await using var context = _fixture.CreateContext();

        return await _factory.BuildTrackFromFile(context, path, "hash", info);
    }

    [Fact]
    public async Task BuildTrackFromFile_FullyTaggedFlac_MapsAllMetadataAndImportsPlays()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("full"), "song.flac"), f =>
        {
            f.Tag.Title = "Song";
            f.Tag.Album = "Record";
            f.Tag.Performers = ["Artist A", " ", "Artist B"];
            f.Tag.AlbumArtists = ["Album Artist"];
            f.Tag.Genres = ["Trance", "House"];
            f.Tag.Lyrics = "la la";
            f.Tag.Year = 1999;
            f.Tag.Track = 3;
            f.Tag.TrackCount = 10;
            f.Tag.Disc = 1;
            f.Tag.DiscCount = 2;
        });
        var added = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc);

        await using var context = _fixture.CreateContext();
        var track = await _factory.BuildTrackFromFile(context, path, "hash", new TrackInfo(7, added));

        track.Title.ShouldBe("Song");
        track.Artists.Select(a => a.Name).ShouldBe(["Artist A", "Artist B"]);
        track.Album.Title.ShouldBe("Record");
        track.Album.AlbumArtists.ShouldHaveSingleItem().Name.ShouldBe("Album Artist");
        track.Album.TotalTracks.ShouldBe(10u);
        track.Album.TotalDiscs.ShouldBe(2u);
        track.Album.CoverArt.ShouldBeNull();
        track.Genres.Select(g => g.Name).ShouldBe(["Trance", "House"]);
        track.Lyrics.ShouldBe("la la");
        track.Year.ShouldBe(1999u);
        track.TrackNumber.ShouldBe(3u);
        track.DiscNumber.ShouldBe(1u);
        track.Format.ShouldBe("FLAC");
        track.SampleRate.ShouldBe(44100);
        track.FileSizeBytes.ShouldBe(new FileInfo(path).Length);
        track.AddedDate.ShouldBe(added);
        track.PlayCount.ShouldBe(7u);
        var play = track.Plays.ShouldHaveSingleItem();
        play.Weight.ShouldBe(7);
        play.IsImported.ShouldBeTrue();
        track.TrackLocations.ShouldHaveSingleItem().IsSupportedFormat.ShouldBeTrue();
        context.ChangeTracker.Entries<ConvertQueueItem>().ShouldBeEmpty();
        context.ChangeTracker.Entries<Track>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task BuildTrackFromFile_UntaggedFile_FallsBackToFileNameUnknownAlbumAndVariousArtists()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("untagged"), "My File.mp3"));

        var before = DateTime.UtcNow;
        var track = await Build(path);

        track.Title.ShouldBe("My File");
        track.Artists.ShouldBeEmpty();
        track.Lyrics.ShouldBeNull();
        track.Album.Title.ShouldBe("Unknown Album");
        track.Album.AlbumArtists.ShouldHaveSingleItem().Name.ShouldBe("Various Artists");
        track.AddedDate.ShouldBeGreaterThanOrEqualTo(before);
        track.PlayCount.ShouldBe(0u);
        track.Plays.ShouldBeEmpty();
    }

    [Fact]
    public async Task BuildTrackFromFile_WithoutAlbumArtists_UsesPerformersAsAlbumArtists()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("performers"), "a.flac"), f =>
        {
            f.Tag.Album = "Solo";
            f.Tag.Performers = ["Performer"];
        });

        var track = await Build(path);

        track.Album.AlbumArtists.ShouldHaveSingleItem().ShouldBeSameAs(track.Artists.Single());
    }

    [Fact]
    public async Task BuildTrackFromFile_PlayCountAtIntMax_DoesNotImportAPlay()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("max"), "a.mp3"));

        var track = await Build(path, new TrackInfo(int.MaxValue, null));

        track.PlayCount.ShouldBe((uint)int.MaxValue);
        track.Plays.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("a.wav")]
    [InlineData("a.m4a", "-c:a", "alac")]
    public async Task BuildTrackFromFile_UnsupportedCodec_QueuesConversion(string fileName, params string[] codec)
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("unsupported"), fileName), null, codec);

        await using var context = _fixture.CreateContext();
        var track = await _factory.BuildTrackFromFile(context, path, "hash", null);

        track.TrackLocations.ShouldHaveSingleItem().IsSupportedFormat.ShouldBeFalse();
        track.BitsPerSample.ShouldBe(16);
        context.ChangeTracker.Entries<ConvertQueueItem>().ShouldHaveSingleItem()
            .Entity.TrackLocation.ShouldBeSameAs(track.TrackLocations.Single());
    }

    [Fact]
    public async Task BuildTrackFromFile_RecordedDateTag_OverridesYear()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("dated"), "a.flac"), null, "-metadata", "date=2020-05-01");

        var track = await Build(path);

        track.RecordedDate.ShouldBe(new DateOnly(2020, 5, 1));
        track.Year.ShouldBe(2020u);
    }

    [Fact]
    public async Task BuildTrackFromFile_SameAlbumTitleAndArtists_ReusesAlbumArtistsAndGenres()
    {
        var dir = AlbumDir("shared");
        var first = await AudioFiles.Create(Path.Combine(dir, "1.flac"), f =>
        {
            f.Tag.Album = "Shared";
            f.Tag.AlbumArtists = ["Band"];
            f.Tag.Performers = ["Band"];
            f.Tag.Genres = ["Rock"];
        });
        var second = await AudioFiles.Create(Path.Combine(dir, "2.flac"), f =>
        {
            f.Tag.Album = "SHARED";
            f.Tag.AlbumArtists = [" band "];
            f.Tag.Performers = ["band"];
            f.Tag.Genres = ["rock"];
        });

        var a = await Build(first);
        var b = await Build(second);

        b.Album.ShouldBeSameAs(a.Album);
        b.Artists.Single().ShouldBeSameAs(a.Artists.Single());
        b.Genres.Single().ShouldBeSameAs(a.Genres.Single());
    }

    [Fact]
    public async Task BuildTrackFromFile_SameAlbumTitleDifferentArtists_CreatesSeparateAlbums()
    {
        var dir = AlbumDir("greatest");
        var first = await AudioFiles.Create(Path.Combine(dir, "1.mp3"), f =>
        {
            f.Tag.Album = "Greatest Hits";
            f.Tag.AlbumArtists = ["Band One"];
        });
        var second = await AudioFiles.Create(Path.Combine(dir, "2.mp3"), f =>
        {
            f.Tag.Album = "Greatest Hits";
            f.Tag.AlbumArtists = ["Band Two"];
        });

        var a = await Build(first);
        var b = await Build(second);

        b.Album.ShouldNotBeSameAs(a.Album);
    }

    [Fact]
    public async Task Reset_ForgetsPreviouslyCreatedEntities()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("reset"), "1.mp3"), f =>
        {
            f.Tag.Album = "X";
            f.Tag.Performers = ["Y"];
            f.Tag.Genres = ["Z"];
        });

        var a = await Build(path);
        _factory.Reset();
        var b = await Build(path);

        b.Album.ShouldNotBeSameAs(a.Album);
        b.Artists.Single().ShouldNotBeSameAs(a.Artists.Single());
        b.Genres.Single().ShouldNotBeSameAs(a.Genres.Single());
    }

    [Fact]
    public async Task PreloadAsync_ReusesExistingDatabaseEntities()
    {
        await using (var seed = _fixture.CreateContext())
        {
            var artist = new Artist { Name = "Band", NormalizedName = "BAND" };
            seed.Albums.Add(new Album { Title = "Existing", AlbumArtists = [artist] });
            seed.Albums.Add(new Album { Title = "Existing", AlbumArtists = [new Artist { Name = "Other", NormalizedName = "OTHER" }] });
            seed.Genres.Add(new Genre { Name = "Jazz" });
            await seed.SaveChangesAsync();
        }

        var path = await AudioFiles.Create(Path.Combine(AlbumDir("preload"), "1.mp3"), f =>
        {
            f.Tag.Album = "Existing";
            f.Tag.AlbumArtists = ["band"];
            f.Tag.Performers = ["Band"];
            f.Tag.Genres = ["jazz"];
        });

        await using var context = _fixture.CreateContext();
        await _factory.PreloadAsync(context, CancellationToken.None);
        var track = await _factory.BuildTrackFromFile(context, path, "hash", null);

        track.Album.Id.ShouldNotBe(0u);
        track.Album.AlbumArtists.Single().NormalizedName.ShouldBe("BAND");
        track.Artists.Single().Id.ShouldNotBe(0u);
        track.Genres.Single().Id.ShouldNotBe(0);
    }

    [Fact]
    public async Task BuildTrackFromFile_FolderCoverJpg_IsUsedAsIsCaseInsensitively()
    {
        var dir = AlbumDir("folder-cover");
        var coverPath = Path.Combine(dir, "Cover.JPG");
        await File.WriteAllBytesAsync(coverPath, await AudioFiles.CreateJpeg(_dir));
        var path = await AudioFiles.Create(Path.Combine(dir, "a.mp3"), f => f.Tag.Album = "Covered");

        var track = await Build(path);

        var cover = track.Album.CoverArt.ShouldNotBeNull();
        cover.Path.ShouldBe(coverPath);
        cover.MimeType.ShouldBe(MediaTypeNames.Image.Jpeg);
        cover.Hash.ShouldBe(await TrackFactory.ComputeHashAsync(coverPath, CancellationToken.None));
    }

    [Fact]
    public async Task BuildTrackFromFile_UnreadableFolderCover_FallsBackToNoCover()
    {
        var dir = AlbumDir("unreadable-cover");
        // a dangling symlink is listed as a file but can't be opened, so hashing it throws
        File.CreateSymbolicLink(Path.Combine(dir, "cover.jpg"), Path.Combine(dir, "missing.jpg"));
        var path = await AudioFiles.Create(Path.Combine(dir, "a.mp3"), f => f.Tag.Album = "Locked");

        var track = await Build(path);

        track.Album.CoverArt.ShouldBeNull();
    }

    [Fact]
    public async Task BuildTrackFromFile_EmbeddedPicture_IsConvertedToWebpOnceAndReused()
    {
        var jpeg = await AudioFiles.CreateJpeg(_dir);
        var dir = AlbumDir("embedded");
        var first = await AudioFiles.Create(Path.Combine(dir, "1.mp3"), f =>
        {
            f.Tag.Album = "Embedded One";
            f.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(jpeg))];
        });
        var second = await AudioFiles.Create(Path.Combine(dir, "2.mp3"), f =>
        {
            f.Tag.Album = "Embedded Two";
            f.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector(jpeg))];
        });
        var expectedHash = Convert.ToHexString(MD5.HashData(jpeg));

        var a = await Build(first);
        var webpPath = Path.Combine(_imagesDir, $"{expectedHash}.webp");
        File.Exists(webpPath).ShouldBeTrue();
        var writtenAt = File.GetLastWriteTimeUtc(webpPath);
        var b = await Build(second);

        foreach (var cover in new[] { a.Album.CoverArt, b.Album.CoverArt })
        {
            cover.ShouldNotBeNull();
            cover.Hash.ShouldBe(expectedHash);
            cover.Path.ShouldBe($"{expectedHash}.webp");
            cover.MimeType.ShouldBe(MediaTypeNames.Image.Webp);
        }

        File.GetLastWriteTimeUtc(webpPath).ShouldBe(writtenAt);
    }

    [Fact]
    public async Task BuildTrackFromFile_CorruptEmbeddedPicture_ImportsWithoutCover()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("bad-picture"), "1.mp3"), f =>
        {
            f.Tag.Album = "Broken Art";
            f.Tag.Pictures = [new TagLib.Picture(new TagLib.ByteVector([1, 2, 3, 4]))];
        });

        var track = await Build(path);

        track.Album.CoverArt.ShouldBeNull();
    }

    [Fact]
    public async Task ReloadTrackMetadataAsync_UpdatesTagsButKeepsLocationsAndPlays()
    {
        var path = await AudioFiles.Create(Path.Combine(AlbumDir("reload"), "a.flac"), f => f.Tag.Title = "New Title");
        var location = new TrackLocation { Path = "/elsewhere.flac" };
        var play = new Play { Weight = 1, DateTime = DateTime.UtcNow };
        var track = new Track
        {
            Title = "Old",
            FileHash = "old",
            MimeType = "x",
            TrackLocations = [location],
            Plays = [play]
        };

        var supported = await _factory.ReloadTrackMetadataAsync(track, path, "new-hash");

        supported.ShouldBeTrue();
        track.Title.ShouldBe("New Title");
        track.FileHash.ShouldBe("new-hash");
        track.TrackLocations.ShouldHaveSingleItem().ShouldBeSameAs(location);
        track.Plays.ShouldHaveSingleItem().ShouldBeSameAs(play);
    }

    [Fact]
    public async Task ComputeHashAsync_ReturnsMd5HashOfFileContents()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "hello world");
            var expected = Convert.ToHexString(MD5.HashData("hello world"u8.ToArray()));

            var hash = await TrackFactory.ComputeHashAsync(path, CancellationToken.None);

            hash.ShouldBe(expected);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ComputeHashAsync_SameContent_ProducesSameHash()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(pathA, "identical content");
            await File.WriteAllTextAsync(pathB, "identical content");

            var hashA = await TrackFactory.ComputeHashAsync(pathA, CancellationToken.None);
            var hashB = await TrackFactory.ComputeHashAsync(pathB, CancellationToken.None);

            hashA.ShouldBe(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Fact]
    public async Task ComputeHashAsync_DifferentContent_ProducesDifferentHash()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(pathA, "content a");
            await File.WriteAllTextAsync(pathB, "content b");

            var hashA = await TrackFactory.ComputeHashAsync(pathA, CancellationToken.None);
            var hashB = await TrackFactory.ComputeHashAsync(pathB, CancellationToken.None);

            hashA.ShouldNotBe(hashB);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    [Theory]
    [InlineData("2020-05-01", "2020-05-01")]
    [InlineData("2020-05-01 00:00:00", "2020-05-01")]
    [InlineData("2020-05-01 13:45:00", "2020-05-01")]
    [InlineData("2020-05-01T00:00:00", "2020-05-01")]
    public void ParseRecordedDate_FullDate_ParsesToDateOnly(string input, string expected)
    {
        var result = TrackFactory.ParseRecordedDate(input);

        result.ShouldBe(DateOnly.Parse(expected));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2020")]
    [InlineData("not a date")]
    public void ParseRecordedDate_YearOnlyOrUnparseable_ReturnsNull(string? input)
    {
        var result = TrackFactory.ParseRecordedDate(input);

        result.ShouldBeNull();
    }
}
