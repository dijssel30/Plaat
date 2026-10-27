using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models;
using Plaat.Db.Repositories;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;
using Genre = Plaat.Db.Models.Genre;
using SmartPlaylistRule = Plaat.Db.Models.SmartPlaylistRule;

namespace Plaat.Db.Tests;

public sealed class SmartPlaylistRepositoryTests
{
    private static SmartPlaylistRuleGroupDto Group(
        SmartPlaylistGroupCombinator combinator,
        IReadOnlyCollection<SmartPlaylistRuleDto>? rules = null,
        IReadOnlyCollection<SmartPlaylistRuleGroupDto>? childGroups = null)
    {
        return new()
        {
            Combinator = combinator,
            Rules = rules?.ToList() ?? [],
            ChildGroups = childGroups?.ToList() ?? []
        };
    }

    private static SmartPlaylistRuleGroupDto SingleRuleGroup(SmartPlaylistRuleDto rule)
    {
        return Group(SmartPlaylistGroupCombinator.And, [rule]);
    }

    [Fact]
    public async Task CreateSmartPlaylist_NameUsedByNormalPlaylist_Throws()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist("Favorites", alice.Id, [], CancellationToken.None);

        await Should.ThrowAsync<System.Data.DuplicateNameException>(() => repo.CreateSmartPlaylist(
            "favorites",
            alice.Id,
            Group(SmartPlaylistGroupCombinator.And),
            null,
            null,
            CancellationToken.None));
    }

    [Fact]
    public async Task CreatePlaylist_NameUsedBySmartPlaylist_Throws()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Favorites",
            alice.Id,
            Group(SmartPlaylistGroupCombinator.And),
            null,
            null,
            CancellationToken.None);

        await Should.ThrowAsync<System.Data.DuplicateNameException>(() =>
            repo.CreatePlaylist("favorites", alice.Id, [], CancellationToken.None));
    }

    [Fact]
    public async Task CreateSmartPlaylist_PlayCountRule_MatchesExpectedTracks()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var loud = fixture.SeedTrack(album, "Loud", "/music/loud.mp3", playCount: 10);
        var quiet = fixture.SeedTrack(album, "Quiet", "/music/quiet.mp3", playCount: 1);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Most played",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto
            {
                PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual,
                PlayCount = 5
            }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Most played", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([loud.Id]);
        quiet.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_AddedDateRule_PersistsAndMatches()
    {
        // Regression test: AddedDateRule/DateTimePlaylistRule was previously missing from the EF
        // TPH discriminator map and could not be persisted at all.
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var recent = fixture.SeedTrack(album, "Recent", "/music/recent.mp3", addedDate: new DateTime(2026, 6, 1));
        var old = fixture.SeedTrack(album, "Old", "/music/old.mp3", addedDate: new DateTime(2020, 1, 1));

        var repo = fixture.CreatePlaylistRepository();

        var exception = await Record.ExceptionAsync(() => repo.CreateSmartPlaylist(
            "Added recently",
            alice.Id,
            SingleRuleGroup(new AddedDateRuleDto
            {
                RuleType = DateComparisonType.HigherOrEqual,
                Value = new DateTimeOffset(new DateTime(2026, 1, 1))
            }),
            null,
            null,
            CancellationToken.None));

        exception.ShouldBeNull();

        var result = await repo.GetPlaylist("Added recently", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([recent.Id]);
        old.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_AddedWithinDaysRule_MatchesTracksAddedRecently()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var recent = fixture.SeedTrack(album, "Recent", "/music/recent.mp3", addedDate: DateTime.UtcNow.AddDays(-2));
        var old = fixture.SeedTrack(album, "Old", "/music/old.mp3", addedDate: DateTime.UtcNow.AddDays(-100));

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Recently added",
            alice.Id,
            SingleRuleGroup(new AddedWithinDaysRuleDto { Days = 7 }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Recently added", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([recent.Id]);
        old.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_NotPlayedInDaysRule_NeverPlayedAndStalePlayMatch_RecentPlayDoesNotMatch()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var neverPlayed = fixture.SeedTrack(album, "Never Played", "/music/never.mp3");
        var stalePlay = fixture.SeedTrack(album, "Stale Play", "/music/stale.mp3");
        var recentPlay = fixture.SeedTrack(album, "Recent Play", "/music/recentplay.mp3");

        fixture.SeedPlay(stalePlay, DateTime.UtcNow.AddDays(-100));
        fixture.SeedPlay(recentPlay, DateTime.UtcNow.AddDays(-1));

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Forgotten tracks",
            alice.Id,
            SingleRuleGroup(new NotPlayedInDaysRuleDto { Days = 30 }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Forgotten tracks", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([neverPlayed.Id, stalePlay.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task CreateSmartPlaylist_DurationRangeRule_MinAndMax_MatchesTracksWithinRange()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var short_ = fixture.SeedTrack(album, "Short", "/music/short.mp3", duration: TimeSpan.FromMinutes(1));
        var medium = fixture.SeedTrack(album, "Medium", "/music/medium.mp3", duration: TimeSpan.FromMinutes(4));
        var long_ = fixture.SeedTrack(album, "Long", "/music/long.mp3", duration: TimeSpan.FromMinutes(10));

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Mid length",
            alice.Id,
            SingleRuleGroup(new DurationRangeRuleDto
            {
                MinDuration = TimeSpan.FromMinutes(2),
                MaxDuration = TimeSpan.FromMinutes(6)
            }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Mid length", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([medium.Id]);
        short_.ShouldNotBeNull();
        long_.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_YearRule_MatchesExpectedTracks()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var newer = fixture.SeedTrack(album, "Newer", "/music/newer.mp3", year: 2020);
        var older = fixture.SeedTrack(album, "Older", "/music/older.mp3", year: 1990);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "2000s+",
            alice.Id,
            SingleRuleGroup(new YearRuleDto { RuleType = PlayCountPlaylistRuleType.HigherOrEqual, Year = 2000 }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("2000s+", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([newer.Id]);
        older.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_BitrateRule_MatchesExpectedTracks()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var highBitrate = fixture.SeedTrack(album, "High", "/music/high.mp3", bitRate: 320);
        var lowBitrate = fixture.SeedTrack(album, "Low", "/music/low.mp3", bitRate: 128);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "High quality",
            alice.Id,
            SingleRuleGroup(new BitrateRuleDto { RuleType = PlayCountPlaylistRuleType.HigherOrEqual, Bitrate = 256 }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("High quality", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([highBitrate.Id]);
        lowBitrate.ShouldNotBeNull();
    }

    /// <summary>
    /// Seeds four tracks rated (by alice) 0/unrated, 1, 3 and 5, then asserts which of them a stars
    /// rule picks up. The unrated one is the interesting case throughout: it has no rating row at
    /// all, but has to compare as 0 stars rather than drop out of every result.
    /// </summary>
    [Theory]
    [InlineData(StarsComparisonType.Equals, 0, new[] { "Unrated" })]
    [InlineData(StarsComparisonType.Equals, 3, new[] { "Three" })]
    [InlineData(StarsComparisonType.HigherOrEqual, 0, new[] { "Unrated", "One", "Three", "Five" })]
    [InlineData(StarsComparisonType.HigherOrEqual, 3, new[] { "Three", "Five" })]
    [InlineData(StarsComparisonType.HigherThan, 3, new[] { "Five" })]
    [InlineData(StarsComparisonType.HigherThan, 0, new[] { "One", "Three", "Five" })]
    [InlineData(StarsComparisonType.LowerOrEqual, 1, new[] { "Unrated", "One" })]
    [InlineData(StarsComparisonType.LowerOrEqual, 0, new[] { "Unrated" })]
    [InlineData(StarsComparisonType.LowerThan, 3, new[] { "Unrated", "One" })]
    [InlineData(StarsComparisonType.LowerThan, 0, new string[0])]
    public async Task CreateSmartPlaylist_StarsRule_MatchesExpectedTracks(
        StarsComparisonType ruleType,
        int stars,
        string[] expectedTitles)
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");

        fixture.SeedTrack(album, "Unrated", "/music/unrated.mp3");
        fixture.SeedRating(fixture.SeedTrack(album, "One", "/music/one.mp3"), alice, 1);
        fixture.SeedRating(fixture.SeedTrack(album, "Three", "/music/three.mp3"), alice, 3);
        fixture.SeedRating(fixture.SeedTrack(album, "Five", "/music/five.mp3"), alice, 5);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "By rating",
            alice.Id,
            SingleRuleGroup(new StarsRuleDto { RuleType = ruleType, Stars = stars }),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("By rating", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Title).OrderBy(t => t).ShouldBe(expectedTitles.OrderBy(t => t));
    }

    [Fact]
    public async Task CreateSmartPlaylist_StarsRule_UsesTheOwnersOwnRatings()
    {
        // Ratings are per user, so the same rule must resolve against the playlist's owner - not
        // against whoever else happens to have rated the same track.
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Test Album");

        var alicesFavorite = fixture.SeedTrack(album, "Alice's", "/music/alice.mp3");
        var bobsFavorite = fixture.SeedTrack(album, "Bob's", "/music/bob.mp3");

        fixture.SeedRating(alicesFavorite, alice, 5);
        fixture.SeedRating(bobsFavorite, bob, 5);

        var repo = fixture.CreatePlaylistRepository();
        var rules = SingleRuleGroup(new StarsRuleDto { RuleType = StarsComparisonType.Equals, Stars = 5 });

        await repo.CreateSmartPlaylist("Top rated", alice.Id, rules, null, null, CancellationToken.None);
        await repo.CreateSmartPlaylist("Top rated", bob.Id, rules, null, null, CancellationToken.None);

        var forAlice = await repo.GetPlaylist("Top rated", alice.Id, CancellationToken.None);
        var forBob = await repo.GetPlaylist("Top rated", bob.Id, CancellationToken.None);

        forAlice.ShouldNotBeNull();
        forAlice.Tracks.Select(t => t.Id).ShouldBe([alicesFavorite.Id]);

        forBob.ShouldNotBeNull();
        forBob.Tracks.Select(t => t.Id).ShouldBe([bobsFavorite.Id]);
    }

    [Fact]
    public async Task CreateSmartPlaylist_StarsRule_PersistsAndRoundTripsThroughGetRules()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "By rating",
            alice.Id,
            SingleRuleGroup(new StarsRuleDto { RuleType = StarsComparisonType.LowerThan, Stars = 2 }),
            null,
            null,
            CancellationToken.None);

        var created = await repo.GetAllPlaylists(alice.Id, CancellationToken.None);
        var rules = await repo.GetSmartPlaylistRules(created.Single().Id, alice.Id, CancellationToken.None);

        rules.ShouldNotBeNull();
        var rule = rules.RootGroup.Rules.OfType<StarsRuleDto>().Single();
        rule.RuleType.ShouldBe(StarsComparisonType.LowerThan);
        rule.Stars.ShouldBe(2);
    }

    /// <summary>
    /// A duration rule forces the whole tree to be evaluated client-side rather than in SQL, so the
    /// stars half has to work off the loaded entities too - which only holds if the client-side load
    /// includes the rating rows.
    /// </summary>
    [Fact]
    public async Task CreateSmartPlaylist_StarsRuleAlongsideDurationRule_StillMatchesOnRating()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");

        var shortAndRated = fixture.SeedTrack(
            album, "Short rated", "/music/short-rated.mp3", duration: TimeSpan.FromMinutes(3));
        var shortAndUnrated = fixture.SeedTrack(
            album, "Short unrated", "/music/short-unrated.mp3", duration: TimeSpan.FromMinutes(3));
        var longAndRated = fixture.SeedTrack(
            album, "Long rated", "/music/long-rated.mp3", duration: TimeSpan.FromMinutes(20));

        fixture.SeedRating(shortAndRated, alice, 5);
        fixture.SeedRating(longAndRated, alice, 5);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Short favourites",
            alice.Id,
            Group(SmartPlaylistGroupCombinator.And,
            [
                new StarsRuleDto { RuleType = StarsComparisonType.HigherOrEqual, Stars = 4 },
                new DurationRangeRuleDto { MaxDuration = TimeSpan.FromMinutes(5) }
            ]),
            null,
            null,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Short favourites", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([shortAndRated.Id]);
        shortAndUnrated.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetSmartPlaylist_NestedAndOrGroups_TwoLevelsDeep_MatchesExpectedTracks()
    {
        // Root OR-group: (Title contains "Anthem") OR (PlayCount >= 5 AND Genre == "Rock")
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");

        var matchesTitleOnly = fixture.SeedTrack(album, "Anthem of Us", "/music/anthem.mp3", playCount: 0);
        var matchesNestedAnd = fixture.SeedTrack(album, "Rock Song", "/music/rock.mp3", playCount: 10);
        var matchesNeither = fixture.SeedTrack(album, "Quiet Pop", "/music/pop.mp3", playCount: 1);

        await using (var ctx = fixture.CreateContext())
        {
            var trackInDb = await ctx.Tracks.Include(t => t.Genres).SingleAsync(t => t.Id == matchesNestedAnd.Id);
            trackInDb.Genres.Add(new Genre { Name = "Rock" });
            await ctx.SaveChangesAsync();
        }

        var rootGroup = Group(
            SmartPlaylistGroupCombinator.Or,
            rules: [new TitlePlaylistRuleDto { TextMatchType = TextMatchType.Contains, Value = "Anthem" }],
            childGroups:
            [
                Group(SmartPlaylistGroupCombinator.And, rules:
                [
                    new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 },
                    new GenrePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Rock" }
                ])
            ]);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist("Nested rules", alice.Id, rootGroup, null, null, CancellationToken.None);

        var result = await repo.GetPlaylist("Nested rules", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([matchesTitleOnly.Id, matchesNestedAnd.Id], ignoreOrder: true);
        matchesNeither.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetSmartPlaylist_EmptyChildGroup_ActsAsIdentityForParentCombinator()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "A Track", "/music/a.mp3", playCount: 10);

        // AND-root with a real rule plus an empty AND-child-group: the empty child must not narrow
        // the result (identity element for AND is "true").
        var andRoot = Group(
            SmartPlaylistGroupCombinator.And,
            rules: [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 1 }],
            childGroups: [Group(SmartPlaylistGroupCombinator.And)]);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist("And with empty child", alice.Id, andRoot, null, null, CancellationToken.None);

        var andResult = await repo.GetPlaylist("And with empty child", alice.Id, CancellationToken.None);
        andResult.ShouldNotBeNull();
        andResult.Tracks.Select(t => t.Id).ShouldBe([track.Id]);

        // OR-root with a real rule plus an empty OR-child-group: the empty child must not widen the
        // result (identity element for OR is "false") - otherwise every track would match.
        var otherTrack = fixture.SeedTrack(album, "Other Track", "/music/other.mp3", playCount: 0);

        var orRoot = Group(
            SmartPlaylistGroupCombinator.Or,
            rules: [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }],
            childGroups: [Group(SmartPlaylistGroupCombinator.Or)]);

        await repo.CreateSmartPlaylist("Or with empty child", alice.Id, orRoot, null, null, CancellationToken.None);

        var orResult = await repo.GetPlaylist("Or with empty child", alice.Id, CancellationToken.None);
        orResult.ShouldNotBeNull();
        orResult.Tracks.Select(t => t.Id).ShouldBe([track.Id]);
        otherTrack.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetSmartPlaylist_Limit_ReturnsTopNOrderedByAddedDateDescending()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var oldest = fixture.SeedTrack(album, "Oldest", "/music/oldest.mp3", addedDate: new DateTime(2020, 1, 1));
        var middle = fixture.SeedTrack(album, "Middle", "/music/middle.mp3", addedDate: new DateTime(2022, 1, 1));
        var newest = fixture.SeedTrack(album, "Newest", "/music/newest.mp3", addedDate: new DateTime(2024, 1, 1));

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Top 2 recent",
            alice.Id,
            Group(SmartPlaylistGroupCombinator.And),
            2,
            SmartPlaylistLimitOrderByDto.RecentlyAdded,
            CancellationToken.None);

        var result = await repo.GetPlaylist("Top 2 recent", alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([newest.Id, middle.Id]);
        oldest.ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateSmartPlaylist_PopulatesTrackCountImmediately()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "A", "/music/a.mp3", playCount: 10);
        fixture.SeedTrack(album, "B", "/music/b.mp3", playCount: 10);
        fixture.SeedTrack(album, "C", "/music/c.mp3", playCount: 0);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Popular",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);

        var summary = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single();

        summary.TrackCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetSmartPlaylist_WriteThrough_CorrectsStaleCachedCount()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "A", "/music/a.mp3", playCount: 10);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Popular",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);

        await using (var ctx = fixture.CreateContext())
        {
            var playlist = await ctx.SmartPlaylists.SingleAsync(p => p.Name == "Popular");
            playlist.TrackCount = 999;
            await ctx.SaveChangesAsync();
        }

        // A second track that now matches, but the cached count above was deliberately staled to
        // simulate library changes since the count was last calculated.
        fixture.SeedTrack(album, "B", "/music/b.mp3", playCount: 20);

        await repo.GetPlaylist("Popular", alice.Id, CancellationToken.None);

        await using var verifyCtx = fixture.CreateContext();
        var updated = await verifyCtx.SmartPlaylists.SingleAsync(p => p.Name == "Popular");
        updated.TrackCount.ShouldBe(2);
    }

    [Fact]
    public async Task RecalculateAllSmartPlaylistTrackCounts_UpdatesStaleCountsAcrossPlaylistsAndUsers()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "A", "/music/a.mp3", playCount: 10);
        fixture.SeedTrack(album, "B", "/music/b.mp3", playCount: 10);

        var repo = fixture.CreatePlaylistRepository();
        var rule = SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 });
        await repo.CreateSmartPlaylist("Alice's popular", alice.Id, rule, null, null, CancellationToken.None);
        await repo.CreateSmartPlaylist("Bob's popular", bob.Id, rule, null, null, CancellationToken.None);

        await using (var ctx = fixture.CreateContext())
        {
            foreach (var playlist in ctx.SmartPlaylists)
            {
                playlist.TrackCount = 999;
            }

            await ctx.SaveChangesAsync();
        }

        await repo.RecalculateAllSmartPlaylistTrackCounts(CancellationToken.None);

        var aliceSummary = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single();
        var bobSummary = (await repo.GetAllPlaylists(bob.Id, CancellationToken.None)).Single();

        aliceSummary.TrackCount.ShouldBe(2);
        bobSummary.TrackCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetPlaylist_SecondCall_ReturnsCachedTracksWithoutRecompute()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "Loud", "/music/loud.mp3", playCount: 10);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Popular",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);

        var first = await repo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        first.ShouldNotBeNull();
        first.Tracks.Select(t => t.Id).ShouldBe([track.Id]);

        // Mutate the underlying data directly, bypassing every repository, so a fresh recompute
        // would no longer match this track. A second GetPlaylist call still returning it proves the
        // result came from the cache rather than a real query.
        await using (var ctx = fixture.CreateContext())
        {
            (await ctx.Tracks.SingleAsync()).PlayCount = 0;
            await ctx.SaveChangesAsync();
        }

        var second = await repo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        second.ShouldNotBeNull();
        second.Tracks.Select(t => t.Id).ShouldBe([track.Id]);
    }

    [Fact]
    public async Task IncreasePlayCount_InvalidatesSmartPlaylistTrackCache()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "Track", "/music/track.mp3", playCount: 0);

        var playlistRepo = fixture.CreatePlaylistRepository();
        await playlistRepo.CreateSmartPlaylist(
            "Popular",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);

        var before = await playlistRepo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        before.ShouldNotBeNull();
        before.Tracks.ShouldBeEmpty();

        var trackRepo = fixture.CreateTrackRepository();
        for (var i = 0; i < 5; i++)
        {
            await trackRepo.IncreasePlayCount(track.Id, alice.Id, CancellationToken.None);
        }

        var after = await playlistRepo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        after.ShouldNotBeNull();
        after.Tracks.Select(t => t.Id).ShouldBe([track.Id]);
    }

    [Fact]
    public async Task SetRating_InvalidatesSmartPlaylistTrackCache()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "Track", "/music/track.mp3");

        var playlistRepo = fixture.CreatePlaylistRepository();
        await playlistRepo.CreateSmartPlaylist(
            "Top rated",
            alice.Id,
            SingleRuleGroup(new StarsRuleDto { RuleType = StarsComparisonType.HigherOrEqual, Stars = 4 }),
            null,
            null,
            CancellationToken.None);

        var before = await playlistRepo.GetPlaylist("Top rated", alice.Id, CancellationToken.None);
        before.ShouldNotBeNull();
        before.Tracks.ShouldBeEmpty();

        var ratingRepo = fixture.CreateTrackRatingRepository();
        await ratingRepo.SetRating(track.Id, alice.Id, 5, CancellationToken.None);

        var after = await playlistRepo.GetPlaylist("Top rated", alice.Id, CancellationToken.None);
        after.ShouldNotBeNull();
        after.Tracks.Select(t => t.Id).ShouldBe([track.Id]);
    }

    [Fact]
    public async Task RemovePlaylist_InvalidatesTrackCache()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        fixture.SeedTrack(album, "Loud", "/music/loud.mp3", playCount: 10);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Popular",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var warmed = await repo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        warmed.ShouldNotBeNull();
        warmed.Tracks.ShouldNotBeEmpty();

        await repo.RemovePlaylist(playlistId, isSmart: true, alice.Id, CancellationToken.None);

        var afterRemoval = await repo.GetPlaylist("Popular", alice.Id, CancellationToken.None);
        afterRemoval.ShouldBeNull();
    }

    [Fact]
    public async Task GetPlaylist_ReflectsPlayAndRatingChangesMadeThroughOtherRepositories()
    {
        // Proves the shared ILibraryCache instance actually wires PlaylistRepository together with
        // TrackRepository/TrackRatingRepository, not just that each repo's own code calls
        // RemoveByPrefix in isolation.
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var track = fixture.SeedTrack(album, "Track", "/music/track.mp3", playCount: 0);

        var playlistRepo = fixture.CreatePlaylistRepository();
        await playlistRepo.CreateSmartPlaylist(
            "Loved and popular",
            alice.Id,
            Group(SmartPlaylistGroupCombinator.And,
            [
                new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 },
                new StarsRuleDto { RuleType = StarsComparisonType.HigherOrEqual, Stars = 4 }
            ]),
            null,
            null,
            CancellationToken.None);

        var afterCreate = await playlistRepo.GetPlaylist("Loved and popular", alice.Id, CancellationToken.None);
        afterCreate.ShouldNotBeNull();
        afterCreate.Tracks.ShouldBeEmpty();

        var trackRepo = fixture.CreateTrackRepository();
        for (var i = 0; i < 5; i++)
        {
            await trackRepo.IncreasePlayCount(track.Id, alice.Id, CancellationToken.None);
        }

        var afterPlays = await playlistRepo.GetPlaylist("Loved and popular", alice.Id, CancellationToken.None);
        afterPlays.ShouldNotBeNull();
        afterPlays.Tracks.ShouldBeEmpty();

        var ratingRepo = fixture.CreateTrackRatingRepository();
        await ratingRepo.SetRating(track.Id, alice.Id, 5, CancellationToken.None);

        var afterRating = await playlistRepo.GetPlaylist("Loved and popular", alice.Id, CancellationToken.None);
        afterRating.ShouldNotBeNull();
        afterRating.Tracks.Select(t => t.Id).ShouldBe([track.Id]);
    }

    [Fact]
    public async Task GetSmartPlaylistRules_ForOwnPlaylist_ReturnsMatchingTree()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var rootGroup = Group(
            SmartPlaylistGroupCombinator.Or,
            rules: [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 5 }],
            childGroups:
            [
                Group(SmartPlaylistGroupCombinator.And, rules:
                [
                    new GenrePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Rock" }
                ])
            ]);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist("Nested", alice.Id, rootGroup, 10, SmartPlaylistLimitOrderByDto.RecentlyAdded, CancellationToken.None);

        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var result = await repo.GetSmartPlaylistRules(playlistId, alice.Id, CancellationToken.None);

        result.ShouldNotBeNull();
        result.Limit.ShouldBe(10);
        result.LimitOrderBy.ShouldBe(SmartPlaylistLimitOrderByDto.RecentlyAdded);
        result.RootGroup.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        result.RootGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCount.ShouldBe(5u);
        result.RootGroup.ChildGroups.ShouldHaveSingleItem();
        var childGroup = result.RootGroup.ChildGroups.Single();
        childGroup.Combinator.ShouldBe(SmartPlaylistGroupCombinator.And);
        childGroup.Rules.OfType<GenrePlaylistRuleDto>().Single().Value.ShouldBe("Rock");
    }

    [Fact]
    public async Task GetSmartPlaylistRules_ForAnotherUsersPlaylist_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Alice's list", alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }),
            null, null, CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var result = await repo.GetSmartPlaylistRules(playlistId, bob.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetSmartPlaylistRules_ForUnknownId_ReturnsNull()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();

        var result = await repo.GetSmartPlaylistRules(999, alice.Id, CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateSmartPlaylist_ReplacesRuleTreeAndRecalculatesCount()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var album = fixture.SeedAlbum("Test Album");
        var loud = fixture.SeedTrack(album, "Loud", "/music/loud.mp3", playCount: 10);
        var quiet = fixture.SeedTrack(album, "Quiet", "/music/quiet.mp3", playCount: 1);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Playlist",
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherOrEqual, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        // Warm the cached track array under the old rule before updating, so this test also proves
        // UpdateSmartPlaylist invalidates it rather than just correctly setting the persisted rules.
        var beforeUpdate = await repo.GetPlaylist("Playlist", alice.Id, CancellationToken.None);
        beforeUpdate.ShouldNotBeNull();
        beforeUpdate.Tracks.Select(t => t.Id).ShouldBe([loud.Id]);

        var updated = await repo.UpdateSmartPlaylist(
            playlistId,
            alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerThan, PlayCount = 5 }),
            null,
            null,
            CancellationToken.None);

        updated.ShouldBeTrue();

        var result = await repo.GetPlaylist("Playlist", alice.Id, CancellationToken.None);
        result.ShouldNotBeNull();
        result.Tracks.Select(t => t.Id).ShouldBe([quiet.Id]);
        result.TrackCount.ShouldBe(1);
        loud.ShouldNotBeNull();

        var rules = await repo.GetSmartPlaylistRules(playlistId, alice.Id, CancellationToken.None);
        rules.ShouldNotBeNull();
        rules.RootGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCountPlaylistRuleType
            .ShouldBe(PlayCountPlaylistRuleType.LowerThan);
    }

    [Fact]
    public async Task UpdateSmartPlaylist_DoesNotLeakOldRuleGroupRows()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Playlist", alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }),
            null, null, CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.UpdateSmartPlaylist(
            playlistId, alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerThan, PlayCount = 1 }),
            null, null, CancellationToken.None);

        await using var ctx = fixture.CreateContext();
        var groupCount = await ctx.Set<Models.SmartPlaylistRuleGroup>().CountAsync();
        var ruleCount = await ctx.Set<SmartPlaylistRule>().CountAsync();

        groupCount.ShouldBe(1);
        ruleCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateSmartPlaylist_ForAnotherUsersPlaylist_ReturnsFalseAndDoesNotModifyIt()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");
        var bob = fixture.SeedUser("bob");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Alice's list", alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }),
            null, null, CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var updated = await repo.UpdateSmartPlaylist(
            playlistId, bob.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerThan, PlayCount = 1 }),
            null, null, CancellationToken.None);

        updated.ShouldBeFalse();

        var rules = await repo.GetSmartPlaylistRules(playlistId, alice.Id, CancellationToken.None);
        rules.ShouldNotBeNull();
        rules.RootGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCountPlaylistRuleType
            .ShouldBe(PlayCountPlaylistRuleType.HigherThan);
    }

    [Fact]
    public async Task UpdateSmartPlaylist_ForUnknownId_ReturnsFalse()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();

        var updated = await repo.UpdateSmartPlaylist(
            999, alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }),
            null, null, CancellationToken.None);

        updated.ShouldBeFalse();
    }

    [Fact]
    public async Task DuplicatePlaylist_SmartPlaylist_CopiesRuleTreeAndLimit()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var rootGroup = Group(
            SmartPlaylistGroupCombinator.Or,
            rules: [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 5 }],
            childGroups:
            [
                Group(SmartPlaylistGroupCombinator.And, rules:
                [
                    new GenrePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Rock" }
                ])
            ]);

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist("Nested", alice.Id, rootGroup, 10, SmartPlaylistLimitOrderByDto.RecentlyAdded, CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var duplicated = await repo.DuplicatePlaylist(
            playlistId, true, alice.Id, "Nested (copy)", CancellationToken.None);

        duplicated.ShouldBeTrue();
        var copyId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None))
            .Single(pl => pl.PlaylistName == "Nested (copy)").Id;

        var copyRules = await repo.GetSmartPlaylistRules(copyId, alice.Id, CancellationToken.None);
        copyRules.ShouldNotBeNull();
        copyRules.Limit.ShouldBe(10);
        copyRules.LimitOrderBy.ShouldBe(SmartPlaylistLimitOrderByDto.RecentlyAdded);
        copyRules.RootGroup.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        copyRules.RootGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCount.ShouldBe(5u);
        copyRules.RootGroup.ChildGroups.Single().Rules.OfType<GenrePlaylistRuleDto>().Single().Value.ShouldBe("Rock");
    }

    /// <summary>
    /// The copy gets its own rule-group rows rather than adopting the original's, so editing one
    /// leaves the other untouched - and deleting one doesn't cascade into the other.
    /// </summary>
    [Fact]
    public async Task DuplicatePlaylist_SmartPlaylist_CopyHasItsOwnRuleTree()
    {
        using var fixture = new DbTestFixture();
        var alice = fixture.SeedUser("alice");

        var repo = fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist(
            "Often played", alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 5 }),
            null, null, CancellationToken.None);
        var playlistId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.DuplicatePlaylist(playlistId, true, alice.Id, "Copy", CancellationToken.None);
        var copyId = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None))
            .Single(pl => pl.PlaylistName == "Copy").Id;

        await repo.UpdateSmartPlaylist(
            copyId, alice.Id,
            SingleRuleGroup(new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerThan, PlayCount = 1 }),
            null, null, CancellationToken.None);

        var originalRules = await repo.GetSmartPlaylistRules(playlistId, alice.Id, CancellationToken.None);
        originalRules.ShouldNotBeNull();
        var originalRule = originalRules.RootGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single();
        originalRule.PlayCountPlaylistRuleType.ShouldBe(PlayCountPlaylistRuleType.HigherThan);
        originalRule.PlayCount.ShouldBe(5u);
    }

    private sealed class UnhandledTestRule : SmartPlaylistRule;

    [Fact]
    public void BuildRulePredicate_UnhandledRuleType_Throws()
    {
        var exception = Record.Exception(() => PlaylistRepository.BuildRulePredicate(new UnhandledTestRule(), userId: 1));

        exception.ShouldBeOfType<InvalidOperationException>();
    }
}
