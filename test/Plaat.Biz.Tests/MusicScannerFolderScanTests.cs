using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Db.Models;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

/// <summary>
/// Folder/file scanning and metadata reloading against real (ffmpeg-generated, TagLib-tagged)
/// audio files.
/// </summary>
public sealed class MusicScannerFolderScanTests : IDisposable
{
    private readonly BizTestFixture _fixture = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("plaat-scan-").FullName;
    private readonly string _music;
    private readonly ILibraryCache _cache = Substitute.For<ILibraryCache>();
    private readonly ITrackRepository _trackRepository = Substitute.For<ITrackRepository>();

    public MusicScannerFolderScanTests()
    {
        _music = Directory.CreateDirectory(Path.Combine(_dir, "music")).FullName;
    }

    public void Dispose()
    {
        _fixture.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private MusicScanner BuildScanner(string? precalculatedHashCsvPath = null)
    {
        var options = new PlaatOptions
        {
            ImagesStoragePath = Directory.CreateDirectory(Path.Combine(_dir, "images")).FullName,
            TargetReconvertedPath = Path.Combine(_dir, "converted"),
            PrecalculatedHashCsvPath = precalculatedHashCsvPath,
            JwtOptions = new JwtOptions { Key = "test-key" }
        };

        return new MusicScanner(
            _fixture.DbFactory,
            NullLogger<MusicScanner>.Instance,
            NullLogger<TrackFactory>.Instance,
            options,
            new MediaInfoParser(NullLogger<MediaInfoParser>.Instance),
            _cache,
            _trackRepository,
            Substitute.For<IAlbumRepository>(),
            Substitute.For<IArtistRepository>(),
            new FilePathReplacer(options));
    }

    private Task<string> Song(string relativePath, string title, params string[] codec)
    {
        var path = Path.Combine(_music, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        return AudioFiles.Create(path, f =>
        {
            f.Tag.Title = title;
            f.Tag.Album = "Album";
            f.Tag.Performers = ["Artist"];
            f.Tag.Genres = ["Genre"];
        }, codec);
    }

    private async Task<List<Track>> AllTracks()
    {
        await using var context = _fixture.CreateContext();

        return await context.Tracks
            .Include(t => t.TrackLocations)
            .Include(t => t.Album)
            .OrderBy(t => t.Title)
            .ToListAsync();
    }

    [Fact]
    public async Task LoadFilesFromFolder_MissingFolder_DoesNothing()
    {
        await BuildScanner().LoadFilesFromFolder(Path.Combine(_dir, "nope"), CancellationToken.None);

        (await AllTracks()).ShouldBeEmpty();
        await _trackRepository.DidNotReceiveWithAnyArgs().RefreshListCache(CancellationToken.None);
    }

    [Fact]
    public async Task LoadFilesFromFolder_ImportsSupportedAudioRecursivelyAndIgnoresOtherFiles()
    {
        await Song("a.mp3", "One");
        await Song("sub/dir/b.flac", "Two");
        await File.WriteAllTextAsync(Path.Combine(_music, "notes.txt"), "not music");

        await BuildScanner().LoadFilesFromFolder(_music, CancellationToken.None);

        var tracks = await AllTracks();
        tracks.Select(t => t.Title).ShouldBe(["One", "Two"]);
        tracks.Select(t => t.Album.Id).Distinct().ShouldHaveSingleItem();
        await _trackRepository.Received().RefreshListCache(Arg.Any<CancellationToken>());
        _cache.Received().Remove(CacheKeys.LibraryStats);
        _cache.Received().RemoveByPrefix(CacheKeys.SmartPlaylistTracksPrefix);
    }

    [Fact]
    public async Task LoadFilesFromFolder_UnsupportedCodec_QueuesConversion()
    {
        await Song("a.wav", "Raw");

        await BuildScanner().LoadFilesFromFolder(_music, CancellationToken.None);

        await using var context = _fixture.CreateContext();
        var item = await context.ConvertQueueItems.Include(i => i.TrackLocation).SingleAsync();
        item.TrackLocation.IsSupportedFormat.ShouldBeFalse();
    }

    [Fact]
    public async Task LoadFilesFromFolder_IdenticalFilesInTwoPlaces_BecomeOneTrackWithTwoLocations()
    {
        var original = await Song("a.mp3", "Same");
        var copy = Path.Combine(_music, "copy", "a.mp3");
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(original, copy);

        await BuildScanner().LoadFilesFromFolder(_music, CancellationToken.None);

        var track = (await AllTracks()).ShouldHaveSingleItem();
        track.TrackLocations.Select(l => l.Path).Order().ShouldBe(new[] { original, copy }.Order());
    }

    [Fact]
    public async Task LoadFilesFromFolder_UnchangedFileOnRescan_IsLeftAlone()
    {
        await Song("a.mp3", "Stable");
        var scanner = BuildScanner();
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        (await AllTracks()).ShouldHaveSingleItem().TrackLocations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LoadFilesFromFolder_FileSizeChangedOnRescan_ReloadsMetadata()
    {
        var path = await Song("a.mp3", "Before");
        var scanner = BuildScanner();
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        // re-encode with a longer duration so the size really changes (TagLib pads tags in place)
        await Song("a.mp3", "After", "-t", "0.5");

        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        var track = (await AllTracks()).ShouldHaveSingleItem();
        track.Title.ShouldBe("After");
        track.FileHash.ShouldBe(await TrackFactory.ComputeHashAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task LoadFilesFromFolder_ChangedUnsupportedFile_QueuesConversionOnlyOnce()
    {
        var path = await Song("a.wav", "Raw");
        var scanner = BuildScanner();
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        await File.AppendAllBytesAsync(path, new byte[16]);
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        // a location that isn't queued yet gets queued on reload
        await using (var context = _fixture.CreateContext())
        {
            context.ConvertQueueItems.RemoveRange(context.ConvertQueueItems);
            await context.SaveChangesAsync();
        }

        await File.AppendAllBytesAsync(path, new byte[16]);
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        await using var verify = _fixture.CreateContext();
        (await verify.ConvertQueueItems.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task LoadFilesFromFolder_ChangedFileThatCanNoLongerBeRead_KeepsTrackAndContinues()
    {
        var path = await Song("a.mp3", "Soon Broken");
        var scanner = BuildScanner();
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);

        await File.WriteAllTextAsync(path, "no longer audio");
        await Should.NotThrowAsync(() => scanner.LoadFilesFromFolder(_music, CancellationToken.None));

        (await AllTracks()).ShouldHaveSingleItem().Title.ShouldBe("Soon Broken");
    }

    [Fact]
    public async Task LoadFilesFromFolder_UsesPrecalculatedHashesAndAppendsNewOnes()
    {
        var known = await Song("known.mp3", "Known");
        var fresh = await Song("fresh.mp3", "Fresh");
        var precalculated = new string('A', 32);
        var csvPath = Path.Combine(_dir, "hashes.csv");
        await File.WriteAllLinesAsync(csvPath, [$"\"{known}\",\"{precalculated}\"", "\"/ignored.mp3\",\"too-short\""]);

        await BuildScanner(csvPath).LoadFilesFromFolder(_music, CancellationToken.None);

        var tracks = await AllTracks();
        tracks.Single(t => t.Title == "Known").FileHash.ShouldBe(precalculated);
        var freshHash = await TrackFactory.ComputeHashAsync(fresh, CancellationToken.None);
        tracks.Single(t => t.Title == "Fresh").FileHash.ShouldBe(freshHash);
        (await File.ReadAllLinesAsync(csvPath)).ShouldContain($"\"{fresh}\",\"{freshHash}\"");
    }

    [Fact]
    public async Task LoadFilesFromFolder_PrecalculatedCsvConfiguredButMissing_HashesNormally()
    {
        await Song("a.mp3", "A");

        await BuildScanner(Path.Combine(_dir, "missing.csv")).LoadFilesFromFolder(_music, CancellationToken.None);

        (await AllTracks()).ShouldHaveSingleItem();
        File.Exists(Path.Combine(_dir, "missing.csv")).ShouldBeFalse();
    }

    [Fact]
    public async Task LoadFiles_SkipsMissingPathsAndDoesNotDuplicateARepeatedPath()
    {
        var path = await Song("a.mp3", "A");
        var csvPath = Path.Combine(_dir, "hashes.csv");
        await File.WriteAllTextAsync(csvPath, string.Empty);

        await BuildScanner(csvPath).LoadFiles([path, path, Path.Combine(_music, "missing.mp3")], CancellationToken.None);

        (await AllTracks()).ShouldHaveSingleItem().TrackLocations.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task LoadFilesFromCsv_WithDatesAndMissingFiles_ImportsExistingWithAddedDate()
    {
        var path = await Song("a.mp3", "Dated");
        var csvPath = Path.Combine(_dir, "import.csv");
        var hashesPath = Path.Combine(_dir, "hashes.csv");
        await File.WriteAllTextAsync(hashesPath, string.Empty);
        await File.WriteAllLinesAsync(csvPath, [
            $"\"{path}\",4,\"2019-03-04 05:06:07\"",
            $"\"{Path.Combine(_music, "gone.mp3")}\",1"
        ]);

        await BuildScanner(hashesPath).LoadFilesFromCsv(csvPath, CancellationToken.None);

        var track = (await AllTracks()).ShouldHaveSingleItem();
        track.AddedDate.ShouldBe(new DateTime(2019, 3, 4, 5, 6, 7));
        track.PlayCount.ShouldBe(4u);
    }

    [Fact]
    public async Task ReloadTrackMetadata_UnknownTrack_ReturnsFalse()
    {
        (await BuildScanner().ReloadTrackMetadata(999, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task ReloadTrackMetadata_NoLocationOnDisk_ReturnsFalse()
    {
        var track = _fixture.SeedTrackWithLocation(Path.Combine(_music, "gone.mp3"));

        (await BuildScanner().ReloadTrackMetadata(track.Id, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task ReloadTrackMetadata_ExistingFile_RereadsTagsAndRefreshesCaches()
    {
        var path = await Song("a.mp3", "From Disk");
        var track = _fixture.SeedTrackWithLocation(path, title: "Stale");

        var result = await BuildScanner().ReloadTrackMetadata(track.Id, CancellationToken.None);

        result.ShouldBeTrue();
        (await AllTracks()).ShouldHaveSingleItem().Title.ShouldBe("From Disk");
        await _trackRepository.Received().RefreshListCache(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReloadTrackMetadata_TrackAlreadyLinkedToItsArtistsAndGenres_Succeeds()
    {
        var path = await Song("a.mp3", "Scanned");
        var scanner = BuildScanner();
        await scanner.LoadFilesFromFolder(_music, CancellationToken.None);
        var track = (await AllTracks()).ShouldHaveSingleItem();

        (await scanner.ReloadTrackMetadata(track.Id, CancellationToken.None)).ShouldBeTrue();

        await using var context = _fixture.CreateContext();
        var reloaded = await context.Tracks.Include(t => t.Artists).Include(t => t.Genres).SingleAsync();
        reloaded.Artists.ShouldHaveSingleItem().Name.ShouldBe("Artist");
        reloaded.Genres.ShouldHaveSingleItem().Name.ShouldBe("Genre");
    }

    [Fact]
    public async Task ReloadTrackMetadata_UnsupportedFile_MarksLocationAndQueuesConversion()
    {
        var path = await Song("a.wav", "Raw");
        var track = _fixture.SeedTrackWithLocation(path);

        (await BuildScanner().ReloadTrackMetadata(track.Id, CancellationToken.None)).ShouldBeTrue();

        await using var context = _fixture.CreateContext();
        (await context.TrackLocations.SingleAsync()).IsSupportedFormat.ShouldBeFalse();
        (await context.ConvertQueueItems.CountAsync()).ShouldBe(1);
    }
}
