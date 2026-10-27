using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Plaat.Biz.Options;
using Plaat.Biz.Scanners;
using Plaat.Biz.Tests.Helpers;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class ItunesLibraryImporterTests
{
    private static PlaatOptions BuildOptions()
    {
        return new()
        {
            ImagesStoragePath = "/images",
            TargetReconvertedPath = "/converted",
            JwtOptions = new JwtOptions { Key = "test-key" }
        };
    }

    private static string WriteLibraryXml(params string[] trackDicts)
    {
        var path = Path.GetTempFileName();

        var tracksBody = string.Join(Environment.NewLine, trackDicts.Select((d, i) => $"""
            <key>{i + 1}</key>
            {d}
            """));

        File.WriteAllText(path, $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
                <key>Tracks</key>
                <dict>
                    {tracksBody}
                </dict>
            </dict>
            </plist>
            """);

        return path;
    }

    private static string TrackDict(
        string location,
        int? rating = null,
        bool ratingComputed = false,
        int? playCount = null,
        int? startTimeMs = null,
        int? stopTimeMs = null)
    {
        var fields = new List<string> { $"<key>Location</key><string>{location}</string>" };

        if (rating is { } r)
        {
            fields.Add($"<key>Rating</key><integer>{r}</integer>");
        }

        if (ratingComputed)
        {
            fields.Add("<key>Rating Computed</key><true/>");
        }

        if (playCount is { } pc)
        {
            fields.Add($"<key>Play Count</key><integer>{pc}</integer>");
        }

        if (startTimeMs is { } start)
        {
            fields.Add($"<key>Start Time</key><integer>{start}</integer>");
        }

        if (stopTimeMs is { } stop)
        {
            fields.Add($"<key>Stop Time</key><integer>{stop}</integer>");
        }

        return $"<dict>{string.Join(Environment.NewLine, fields)}</dict>";
    }

    private static ItunesLibraryImporter BuildImporter(
        BizTestFixture fixture,
        out ITrackRatingRepository ratingRepository,
        IMusicScanner? scanner = null)
    {
        ratingRepository = Substitute.For<ITrackRatingRepository>();

        return new ItunesLibraryImporter(
            scanner ?? Substitute.For<IMusicScanner>(),
            fixture.DbFactory,
            new FilePathReplacer(BuildOptions()),
            ratingRepository,
            NullLogger<ItunesLibraryImporter>.Instance);
    }

    [Fact]
    public async Task Import_MatchingTrackWithActualRating_SetsStarRating()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", rating: 80));

        try
        {
            var importer = BuildImporter(fixture, out var ratings);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            await ratings.Received(1).SetRating(track.Id, 1, 4, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_ComputedRating_IsNotImported()
    {
        using var fixture = new BizTestFixture();
        fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", rating: 100, ratingComputed: true));

        try
        {
            var importer = BuildImporter(fixture, out var ratings);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            await ratings.DidNotReceive().SetRating(
                Arg.Any<uint>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_XmlPlayCountHigherThanImported_RaisesWeightAndTotal()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        await using (var context = fixture.CreateContext())
        {
            var t = context.Tracks.First(t => t.Id == track.Id);
            t.PlayCount = 4;
            context.Plays.Add(new Db.Models.Play { TrackId = t.Id, Weight = 3, IsImported = true, DateTime = DateTime.UtcNow });
            context.Plays.Add(new Db.Models.Play { TrackId = t.Id, Weight = 1, DateTime = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", playCount: 10));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            context.Tracks.First(t => t.Id == track.Id).PlayCount.ShouldBe(11u);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_XmlPlayCountLowerThanImported_DoesNotUpdate()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        await using (var context = fixture.CreateContext())
        {
            var t = context.Tracks.First(t => t.Id == track.Id);
            t.PlayCount = 10;
            context.Plays.Add(new Db.Models.Play { TrackId = t.Id, Weight = 10, IsImported = true, DateTime = DateTime.UtcNow });
            await context.SaveChangesAsync();
        }

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", playCount: 3));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            context.Tracks.First(t => t.Id == track.Id).PlayCount.ShouldBe(10u);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_StartAndStopTimePresent_SetsTrimPoints()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromMinutes(5));

        var xmlPath = WriteLibraryXml(
            TrackDict("file://localhost/music/a.mp3", startTimeMs: 1500, stopTimeMs: 200000));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            var updated = context.Tracks.First(t => t.Id == track.Id);
            updated.TrimStart.ShouldBe(TimeSpan.FromMilliseconds(1500));
            updated.TrimEnd.ShouldBe(TimeSpan.FromMilliseconds(200000));
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_TrimPointsBeyondTrackDuration_LeavesTrimPointsUnset()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromSeconds(100));

        var xmlPath = WriteLibraryXml(
            TrackDict("file://localhost/music/a.mp3", startTimeMs: 1500, stopTimeMs: 200000));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            var updated = context.Tracks.First(t => t.Id == track.Id);
            updated.TrimStart.ShouldBeNull();
            updated.TrimEnd.ShouldBeNull();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_StartAfterExistingTrimEnd_LeavesTrimPointsUnchanged()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromSeconds(100));

        using (var seed = fixture.CreateContext())
        {
            seed.Tracks.First(t => t.Id == track.Id).TrimEnd = TimeSpan.FromSeconds(10);
            seed.SaveChanges();
        }

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", startTimeMs: 20000));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            var updated = context.Tracks.First(t => t.Id == track.Id);
            updated.TrimStart.ShouldBeNull();
            updated.TrimEnd.ShouldBe(TimeSpan.FromSeconds(10));
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_StartAndStopTimeAreZero_LeavesTrimPointsUnset()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/a.mp3", startTimeMs: 0, stopTimeMs: 0));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            var updated = context.Tracks.First(t => t.Id == track.Id);
            updated.TrimStart.ShouldBeNull();
            updated.TrimEnd.ShouldBeNull();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_StartTimeNotBeforeStopTime_IgnoresBoth()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");

        var xmlPath = WriteLibraryXml(
            TrackDict("file://localhost/music/a.mp3", startTimeMs: 5000, stopTimeMs: 1000));

        try
        {
            var importer = BuildImporter(fixture, out _);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            using var context = fixture.CreateContext();
            var updated = context.Tracks.First(t => t.Id == track.Id);
            updated.TrimStart.ShouldBeNull();
            updated.TrimEnd.ShouldBeNull();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_TrackNotInDb_ImportMissingTracksFalse_DoesNotScanOrApplyData()
    {
        using var fixture = new BizTestFixture();

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/missing.mp3", rating: 100));

        try
        {
            var scanner = Substitute.For<IMusicScanner>();
            var importer = BuildImporter(fixture, out var ratings, scanner);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            await scanner.DidNotReceive().LoadFiles(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
            await ratings.DidNotReceive().SetRating(
                Arg.Any<uint>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_TrackNotInDb_ImportMissingTracksTrue_ScansFileThenAppliesData()
    {
        using var fixture = new BizTestFixture();

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/music/new.mp3", rating: 60));

        try
        {
            var scanner = Substitute.For<IMusicScanner>();
            scanner.LoadFiles(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    // Simulate the scanner adding the track to the database, as MusicScanner would.
                    fixture.SeedTrackWithLocation("/music/new.mp3", "New");
                    return Task.CompletedTask;
                });

            var importer = BuildImporter(fixture, out var ratings, scanner);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: true, CancellationToken.None);

            await scanner.Received(1).LoadFiles(
                Arg.Is<IReadOnlyCollection<string>>(p => p != null && p.Count == 1 && p.Contains("/music/new.mp3")),
                Arg.Any<CancellationToken>());

            using var context = fixture.CreateContext();
            var newTrack = context.Tracks.Single(t => t.Title == "New");
            await ratings.Received(1).SetRating(newTrack.Id, 1, 3, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Fact]
    public async Task Import_WindowsStyleLocation_IsDecodedAndPathReplaced()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("C:/Music/a.mp3", "A");

        var xmlPath = WriteLibraryXml(TrackDict("file://localhost/C:/Music/a.mp3", rating: 100));

        try
        {
            var importer = BuildImporter(fixture, out var ratings);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: false, CancellationToken.None);

            await ratings.Received(1).SetRating(track.Id, 1, 5, Arg.Any<CancellationToken>());
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    private static async Task RunImport(BizTestFixture fixture, string xmlContent, ITrackRatingRepository? ratings = null, IMusicScanner? scanner = null, bool importMissing = false)
    {
        var xmlPath = Path.GetTempFileName();
        await File.WriteAllTextAsync(xmlPath, xmlContent);

        try
        {
            var importer = new ItunesLibraryImporter(
                scanner ?? Substitute.For<IMusicScanner>(),
                fixture.DbFactory,
                new FilePathReplacer(BuildOptions()),
                ratings ?? Substitute.For<ITrackRatingRepository>(),
                NullLogger<ItunesLibraryImporter>.Instance);

            await importer.Import(xmlPath, userId: 1, importMissingTracks: importMissing, CancellationToken.None);
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    [Theory]
    [InlineData("<plist version=\"1.0\"></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>Major Version</key><integer>1</integer></dict></plist>")]
    [InlineData("<plist version=\"1.0\"><dict><key>Tracks</key><array/></dict></plist>")]
    public async Task Import_LibraryWithoutTracksDictionary_DoesNothing(string xml)
    {
        using var fixture = new BizTestFixture();
        var scanner = Substitute.For<IMusicScanner>();
        var ratings = Substitute.For<ITrackRatingRepository>();

        await RunImport(fixture, xml, ratings, scanner, importMissing: true);

        scanner.ReceivedCalls().ShouldBeEmpty();
        ratings.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("<key>Name</key><string>no location</string>")]
    [InlineData("<key>Location</key><integer>5</integer>")]
    [InlineData("<key>Location</key><string>http://example.com/a.mp3</string>")]
    [InlineData("<key>Location</key><string>not a uri at all</string>")]
    [InlineData("<key>Location</key><string></string>")]
    public async Task Import_TrackWithoutUsableFileLocation_IsIgnored(string trackFields)
    {
        using var fixture = new BizTestFixture();
        var scanner = Substitute.For<IMusicScanner>();

        await RunImport(fixture, WrapLibrary($"<dict>{trackFields}<key>Rating</key><integer>100</integer></dict>"), scanner: scanner, importMissing: true);

        scanner.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Import_FieldsWithUnexpectedTypesOrStrayElements_AreTreatedAsAbsent()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A");
        var ratings = Substitute.For<ITrackRatingRepository>();

        await RunImport(fixture, WrapLibrary("""
            <dict>
                <string>stray value without a key</string>
                <string>another stray</string>
                <key>Location</key><string>file://localhost/music/a.mp3</string>
                <key>Rating</key><string>100</string>
                <key>Play Count</key><integer>-3</integer>
                <key>Start Time</key><integer>abc</integer>
            </dict>
            """), ratings);

        ratings.ReceivedCalls().ShouldBeEmpty();
        using var context = fixture.CreateContext();
        var stored = context.Tracks.Single(t => t.Id == track.Id);
        stored.PlayCount.ShouldBe(0u);
        stored.TrimStart.ShouldBeNull();
    }

    [Fact]
    public async Task Import_OnlyStartTime_SetsOnlyTrimStart()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromMinutes(5));

        await RunImport(fixture, WrapLibrary(TrackDict("file://localhost/music/a.mp3", startTimeMs: 1000)));

        using var context = fixture.CreateContext();
        var stored = context.Tracks.Single(t => t.Id == track.Id);
        stored.TrimStart.ShouldBe(TimeSpan.FromSeconds(1));
        stored.TrimEnd.ShouldBeNull();
    }

    [Fact]
    public async Task Import_OnlyStopTime_SetsOnlyTrimEnd()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromMinutes(5));

        await RunImport(fixture, WrapLibrary(TrackDict("file://localhost/music/a.mp3", stopTimeMs: 60000)));

        using var context = fixture.CreateContext();
        var stored = context.Tracks.Single(t => t.Id == track.Id);
        stored.TrimStart.ShouldBeNull();
        stored.TrimEnd.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Import_StopTimeBeyondDurationWithValidStart_IgnoresBoth()
    {
        using var fixture = new BizTestFixture();
        var track = fixture.SeedTrackWithLocation("/music/a.mp3", "A", TimeSpan.FromMinutes(1));

        await RunImport(fixture, WrapLibrary(TrackDict("file://localhost/music/a.mp3", startTimeMs: 1000, stopTimeMs: 120000)));

        using var context = fixture.CreateContext();
        var stored = context.Tracks.Single(t => t.Id == track.Id);
        stored.TrimStart.ShouldBeNull();
        stored.TrimEnd.ShouldBeNull();
    }

    [Fact]
    public async Task Import_MissingTrackThatScannerCannotImport_IsSkipped()
    {
        using var fixture = new BizTestFixture();
        var scanner = Substitute.For<IMusicScanner>();
        var ratings = Substitute.For<ITrackRatingRepository>();

        await RunImport(fixture, WrapLibrary(TrackDict("file://localhost/music/unreadable.mp3", rating: 100)), ratings, scanner, importMissing: true);

        await scanner.Received(1).LoadFiles(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>());
        ratings.ReceivedCalls().ShouldBeEmpty();
    }

    private static string WrapLibrary(string trackDict)
    {
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
                <key>Tracks</key>
                <dict>
                    <key>1</key>
                    {trackDict}
                </dict>
            </dict>
            </plist>
            """;
    }
}
