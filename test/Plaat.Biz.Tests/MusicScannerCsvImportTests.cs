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

public sealed class MusicScannerCsvImportTests : IDisposable
{
    private const string Content = "not really audio";

    private readonly BizTestFixture _fixture = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("plaat-csv-").FullName;

    public void Dispose()
    {
        _fixture.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private MusicScanner BuildScanner()
    {
        var options = new PlaatOptions
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/converted",
            JwtOptions = new JwtOptions { Key = "test-key" }
        };

        return new MusicScanner(
            _fixture.DbFactory,
            NullLogger<MusicScanner>.Instance,
            NullLogger<TrackFactory>.Instance,
            options,
            new MediaInfoParser(NullLogger<MediaInfoParser>.Instance),
            Substitute.For<ILibraryCache>(),
            Substitute.For<ITrackRepository>(),
            Substitute.For<IAlbumRepository>(),
            Substitute.For<IArtistRepository>(),
            new FilePathReplacer(options));
    }

    private string WriteAudioFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, Content);

        return path;
    }

    private async Task<Track> SeedTrackForFile(string path, uint playCount = 0, params Play[] plays)
    {
        await using var context = _fixture.CreateContext();

        var track = new Track
        {
            Title = "T",
            Album = new Album { Title = "A" },
            FileHash = await TrackFactory.ComputeHashAsync(path, CancellationToken.None),
            MimeType = "audio/mpeg",
            FileSizeBytes = new FileInfo(path).Length,
            PlayCount = playCount,
            Plays = plays,
            TrackLocations = [new TrackLocation { Path = path, IsSupportedFormat = true }]
        };

        context.Tracks.Add(track);
        await context.SaveChangesAsync();

        return track;
    }

    private static Play Imported(int weight)
    {
        return new Play { Weight = weight, IsImported = true, DateTime = DateTime.UtcNow };
    }

    private static Play Listened()
    {
        return new Play { Weight = 1, DateTime = DateTime.UtcNow };
    }

    private string WriteCsv(params (string path, uint count)[] rows)
    {
        var csvPath = Path.Combine(_dir, $"{Guid.NewGuid():N}.csv");
        File.WriteAllLines(csvPath, rows.Select(r => $"\"{r.path}\",{r.count}"));

        return csvPath;
    }

    private async Task<Track> Reload(uint trackId)
    {
        await using var context = _fixture.CreateContext();

        return await context.Tracks.Include(t => t.Plays).SingleAsync(t => t.Id == trackId);
    }

    [Fact]
    public async Task LoadFilesFromCsv_KnownTrackWithHigherImportedCount_RaisesWeightAndTotal()
    {
        var path = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(path, 7, Imported(5), Listened(), Listened());

        await BuildScanner().LoadFilesFromCsv(WriteCsv((path, 9)), CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(11u);
        reloaded.Plays.Single(p => p.IsImported).Weight.ShouldBe(9);
        reloaded.Plays.Count.ShouldBe(3);
    }

    [Fact]
    public async Task LoadFilesFromCsv_KnownTrackWithLowerImportedCount_ChangesNothing()
    {
        var path = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(path, 5, Imported(5));

        await BuildScanner().LoadFilesFromCsv(WriteCsv((path, 3)), CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(5u);
        reloaded.Plays.Single().Weight.ShouldBe(5);
    }

    [Fact]
    public async Task LoadFilesFromCsv_SameCsvRunTwice_DoesNotCountDouble()
    {
        var path = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(path, 2, Listened(), Listened());
        var csv = WriteCsv((path, 4));
        var scanner = BuildScanner();

        await scanner.LoadFilesFromCsv(csv, CancellationToken.None);
        await scanner.LoadFilesFromCsv(csv, CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(6u);
        reloaded.Plays.Count(p => p.IsImported).ShouldBe(1);
    }

    [Fact]
    public async Task LoadFilesFromCsv_PlayedOnceWithOnlyWeightOnePlays_IsNotUpdated()
    {
        var path = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(path, 1, Listened());

        await BuildScanner().LoadFilesFromCsv(WriteCsv((path, 20)), CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(1u);
        reloaded.Plays.ShouldNotContain(p => p.IsImported);
    }

    [Fact]
    public async Task LoadFilesFromCsv_NewLocationWithSameHashAsKnownTrack_RaisesKnownTrack()
    {
        var known = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(known, 3, Imported(3));
        var copy = WriteAudioFile("copy.mp3");

        await BuildScanner().LoadFilesFromCsv(WriteCsv((copy, 8)), CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(8u);
        reloaded.Plays.Single(p => p.IsImported).Weight.ShouldBe(8);

        await using var context = _fixture.CreateContext();
        context.TrackLocations.Count(l => l.TrackId == track.Id).ShouldBe(2);
    }

    [Fact]
    public async Task LoadFilesFromCsv_AfterImport_LaterScansDoNotReapplyCsvCounts()
    {
        var path = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(path, 3, Imported(3));
        var scanner = BuildScanner();

        await scanner.LoadFilesFromCsv(WriteCsv((path, 9)), CancellationToken.None);

        // Simulate the stored base weight going back down; a leaked CSV entry would raise it to 9 again.
        await using (var context = _fixture.CreateContext())
        {
            var play = await context.Plays.SingleAsync(p => p.TrackId == track.Id && p.IsImported);
            play.Weight = 2;
            context.Tracks.Single(t => t.Id == track.Id).PlayCount = 2;
            await context.SaveChangesAsync();
        }

        await scanner.LoadFiles([path], CancellationToken.None);

        var reloaded = await Reload(track.Id);
        reloaded.PlayCount.ShouldBe(2u);
        reloaded.Plays.Single(p => p.IsImported).Weight.ShouldBe(2);
    }

    [Fact]
    public async Task LoadFiles_UnreadableFile_DoesNotAbortOtherFiles()
    {
        var known = WriteAudioFile("a.mp3");
        var track = await SeedTrackForFile(known);
        var copy = WriteAudioFile("copy.mp3");

        // A directory with an audio extension exists but cannot be hashed as a file.
        var unreadable = Path.Combine(_dir, "broken.mp3");
        Directory.CreateDirectory(unreadable);

        await Should.NotThrowAsync(() => BuildScanner().LoadFiles([unreadable, copy], CancellationToken.None));

        await using var context = _fixture.CreateContext();
        context.TrackLocations.Count(l => l.TrackId == track.Id).ShouldBe(2);
        context.TrackLocations.ShouldNotContain(l => l.Path == unreadable);
    }

    [Fact]
    public async Task LoadFiles_CancelledToken_StillPropagatesCancellation()
    {
        var path = WriteAudioFile("a.mp3");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => BuildScanner().LoadFiles([path], cts.Token));
    }
}
