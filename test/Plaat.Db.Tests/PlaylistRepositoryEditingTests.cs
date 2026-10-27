using Plaat.Contract.Models;
using Plaat.Db.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

/// <summary>
/// Adding/removing tracks, pinning, and round-tripping every smart playlist rule type.
/// </summary>
public sealed class PlaylistRepositoryEditingTests : IDisposable
{
    private readonly DbTestFixture _fixture = new();

    public void Dispose()
    {
        _fixture.Dispose();
    }

    private async Task<uint> CreatePlaylist(int userId, string name, params uint[] trackIds)
    {
        var repo = _fixture.CreatePlaylistRepository();
        await repo.CreatePlaylist(name, userId, trackIds, CancellationToken.None);

        return (await repo.GetAllPlaylists(userId, CancellationToken.None)).Single(p => p.PlaylistName == name).Id;
    }

    private async Task<IReadOnlyList<uint>> TrackIds(int userId, string name)
    {
        var playlist = await _fixture.CreatePlaylistRepository().GetPlaylist(name, userId, CancellationToken.None);

        return [.. playlist!.Tracks.Select(t => t.Id)];
    }

    [Fact]
    public async Task CreatePlaylist_KeepsTheGivenTrackOrder()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var b = _fixture.SeedTrack(album, "B", "/b.mp3");
        var c = _fixture.SeedTrack(album, "C", "/c.mp3");

        await CreatePlaylist(alice.Id, "List", c.Id, a.Id, b.Id);

        (await TrackIds(alice.Id, "List")).ShouldBe([c.Id, a.Id, b.Id]);
    }

    [Fact]
    public async Task AddToPlaylist_EmptyPlaylist_AddsInOrderIgnoringDuplicatesAndUnknownIds()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var b = _fixture.SeedTrack(album, "B", "/b.mp3");
        var id = await CreatePlaylist(alice.Id, "List");

        await _fixture.CreatePlaylistRepository().AddToPlaylist(id, alice.Id, [b.Id, a.Id, b.Id, 9999], CancellationToken.None);

        (await TrackIds(alice.Id, "List")).ShouldBe([b.Id, a.Id]);
    }

    [Fact]
    public async Task AddToPlaylist_NonEmptyPlaylist_AppendsOnlyTracksNotAlreadyPresent()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var b = _fixture.SeedTrack(album, "B", "/b.mp3");
        var c = _fixture.SeedTrack(album, "C", "/c.mp3");
        var id = await CreatePlaylist(alice.Id, "List", b.Id, a.Id);

        await _fixture.CreatePlaylistRepository().AddToPlaylist(id, alice.Id, [a.Id, c.Id], CancellationToken.None);

        (await TrackIds(alice.Id, "List")).ShouldBe([b.Id, a.Id, c.Id]);
    }

    [Fact]
    public async Task AddToPlaylist_OnlyTracksAlreadyPresent_LeavesPlaylistUnchanged()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var id = await CreatePlaylist(alice.Id, "List", a.Id);

        await _fixture.CreatePlaylistRepository().AddToPlaylist(id, alice.Id, [a.Id], CancellationToken.None);

        (await TrackIds(alice.Id, "List")).ShouldBe([a.Id]);
    }

    [Fact]
    public async Task RemoveFromPlaylist_RemovesOnlyThatTrack()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var b = _fixture.SeedTrack(album, "B", "/b.mp3");
        var id = await CreatePlaylist(alice.Id, "List", a.Id, b.Id);

        await _fixture.CreatePlaylistRepository().RemoveFromPlaylist(id, alice.Id, a.Id, CancellationToken.None);

        (await TrackIds(alice.Id, "List")).ShouldBe([b.Id]);
    }

    [Fact]
    public async Task RemoveFromPlaylist_ForAnotherUsersPlaylist_DoesNothing()
    {
        var alice = _fixture.SeedUser("alice");
        var bob = _fixture.SeedUser("bob");
        var album = _fixture.SeedAlbum("Album");
        var a = _fixture.SeedTrack(album, "A", "/a.mp3");
        var id = await CreatePlaylist(alice.Id, "List", a.Id);

        await _fixture.CreatePlaylistRepository().RemoveFromPlaylist(id, bob.Id, a.Id, CancellationToken.None);

        (await TrackIds(alice.Id, "List")).ShouldBe([a.Id]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetPlaylistPinned_PinsAndUnpinsRegularAndSmartPlaylists(bool isSmart)
    {
        var alice = _fixture.SeedUser("alice");
        var repo = _fixture.CreatePlaylistRepository();
        if (isSmart)
        {
            await repo.CreateSmartPlaylist("List", alice.Id, new SmartPlaylistRuleGroupDto(), null, null, CancellationToken.None);
        }
        else
        {
            await repo.CreatePlaylist("List", alice.Id, [], CancellationToken.None);
        }

        var id = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        await repo.SetPlaylistPinned(id, isSmart, alice.Id, true, CancellationToken.None);
        (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().IsPinned.ShouldBeTrue();

        await repo.SetPlaylistPinned(id, isSmart, alice.Id, false, CancellationToken.None);
        (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().IsPinned.ShouldBeFalse();
    }

    [Fact]
    public async Task DuplicatePlaylist_AnotherUsersSmartPlaylist_ReturnsFalse()
    {
        var alice = _fixture.SeedUser("alice");
        var bob = _fixture.SeedUser("bob");
        var repo = _fixture.CreatePlaylistRepository();
        await repo.CreateSmartPlaylist("Smart", alice.Id, new SmartPlaylistRuleGroupDto(), null, null, CancellationToken.None);
        var id = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        (await repo.DuplicatePlaylist(id, true, bob.Id, "Copy", CancellationToken.None)).ShouldBeFalse();
        (await repo.GetAllPlaylists(bob.Id, CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSmartPlaylistRules_EveryRuleType_RoundTripsThroughStorage()
    {
        var alice = _fixture.SeedUser("alice");
        var repo = _fixture.CreatePlaylistRepository();
        var addedAfter = new DateTimeOffset(2021, 2, 3, 0, 0, 0, TimeSpan.Zero);
        var root = new SmartPlaylistRuleGroupDto
        {
            Combinator = SmartPlaylistGroupCombinator.Or,
            Rules =
            [
                new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerThan, PlayCount = 3 },
                new TitlePlaylistRuleDto { TextMatchType = TextMatchType.StartsWith, Value = "T" },
                new ArtistNamePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Ar" },
                new AlbumNamePlaylistRuleDto { TextMatchType = TextMatchType.EndsWith, Value = "Al" },
                new GenrePlaylistRuleDto { TextMatchType = TextMatchType.Contains, Value = "Ge" },
                new AddedDateRuleDto { RuleType = DateComparisonType.HigherOrEqual, Value = addedAfter },
                new AddedWithinDaysRuleDto { Days = 7 },
                new NotPlayedInDaysRuleDto { Days = 30 },
                new DurationRangeRuleDto { MinDuration = TimeSpan.FromMinutes(1), MaxDuration = TimeSpan.FromMinutes(9) },
                new YearRuleDto { RuleType = PlayCountPlaylistRuleType.HigherThan, Year = 1990 },
                new BitrateRuleDto { RuleType = PlayCountPlaylistRuleType.LowerOrEqual, Bitrate = 256 },
                new StarsRuleDto { RuleType = StarsComparisonType.HigherOrEqual, Stars = 4 }
            ],
            ChildGroups =
            [
                new SmartPlaylistRuleGroupDto
                {
                    Combinator = SmartPlaylistGroupCombinator.And,
                    Rules = [new YearRuleDto { RuleType = PlayCountPlaylistRuleType.LowerThan, Year = 2000 }]
                }
            ]
        };
        await repo.CreateSmartPlaylist("All rules", alice.Id, root, 10, SmartPlaylistLimitOrderByDto.RecentlyAdded, CancellationToken.None);
        var id = (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().Id;

        var rules = await repo.GetSmartPlaylistRules(id, alice.Id, CancellationToken.None);

        rules.ShouldNotBeNull();
        rules.Limit.ShouldBe(10);
        rules.LimitOrderBy.ShouldBe(SmartPlaylistLimitOrderByDto.RecentlyAdded);
        rules.RootGroup.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        var r = rules.RootGroup.Rules.ToList();
        r.Count.ShouldBe(12);
        r.OfType<PlayCountPlaylistRuleDto>().Single().PlayCount.ShouldBe(3u);
        r.OfType<TitlePlaylistRuleDto>().Single().TextMatchType.ShouldBe(TextMatchType.StartsWith);
        r.OfType<ArtistNamePlaylistRuleDto>().Single().Value.ShouldBe("Ar");
        r.OfType<AlbumNamePlaylistRuleDto>().Single().TextMatchType.ShouldBe(TextMatchType.EndsWith);
        r.OfType<GenrePlaylistRuleDto>().Single().Value.ShouldBe("Ge");
        var added = r.OfType<AddedDateRuleDto>().Single();
        added.RuleType.ShouldBe(DateComparisonType.HigherOrEqual);
        added.Value.ShouldBe(addedAfter);
        r.OfType<AddedWithinDaysRuleDto>().Single().Days.ShouldBe(7u);
        r.OfType<NotPlayedInDaysRuleDto>().Single().Days.ShouldBe(30u);
        r.OfType<DurationRangeRuleDto>().Single().MaxDuration.ShouldBe(TimeSpan.FromMinutes(9));
        r.OfType<YearRuleDto>().Single().Year.ShouldBe(1990u);
        r.OfType<BitrateRuleDto>().Single().Bitrate.ShouldBe(256);
        r.OfType<StarsRuleDto>().Single().RuleType.ShouldBe(StarsComparisonType.HigherOrEqual);
        rules.RootGroup.ChildGroups.Single().Rules.OfType<YearRuleDto>().Single().Year.ShouldBe(2000u);
    }

    [Fact]
    public async Task CreateSmartPlaylist_NestedDeeperThanAllowed_Throws()
    {
        var alice = _fixture.SeedUser("alice");
        var group = new SmartPlaylistRuleGroupDto();
        for (var depth = 0; depth < SmartPlaylistRuleGroupLimits.MaxGroupDepth; depth++)
        {
            group = new SmartPlaylistRuleGroupDto { ChildGroups = [group] };
        }

        await Should.ThrowAsync<InvalidOperationException>(() => _fixture.CreatePlaylistRepository()
            .CreateSmartPlaylist("Too deep", alice.Id, group, null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SmartPlaylist_DurationRuleWithLimit_AppliesLimitToClientEvaluatedTracks()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        _fixture.SeedTrack(album, "Old", "/old.mp3", addedDate: new DateTime(2020, 1, 1), duration: TimeSpan.FromMinutes(3));
        var newer = _fixture.SeedTrack(album, "New", "/new.mp3", addedDate: new DateTime(2024, 1, 1), duration: TimeSpan.FromMinutes(3));
        _fixture.SeedTrack(album, "Long", "/long.mp3", addedDate: new DateTime(2025, 1, 1), duration: TimeSpan.FromMinutes(30));
        var repo = _fixture.CreatePlaylistRepository();
        var root = new SmartPlaylistRuleGroupDto
        {
            Rules = [new DurationRangeRuleDto { MaxDuration = TimeSpan.FromMinutes(5) }]
        };

        await repo.CreateSmartPlaylist("Short recent", alice.Id, root, 1, SmartPlaylistLimitOrderByDto.RecentlyAdded, CancellationToken.None);

        var playlist = await repo.GetPlaylist("Short recent", alice.Id, CancellationToken.None);
        playlist!.Tracks.Select(t => t.Id).ShouldBe([newer.Id]);
        (await repo.GetAllPlaylists(alice.Id, CancellationToken.None)).Single().TrackCount.ShouldBe(1);
    }

    [Fact]
    public async Task SmartPlaylist_LimitWithoutOrdering_KeepsAllMatches()
    {
        var alice = _fixture.SeedUser("alice");
        var album = _fixture.SeedAlbum("Album");
        _fixture.SeedTrack(album, "A", "/a.mp3", duration: TimeSpan.FromMinutes(3));
        _fixture.SeedTrack(album, "B", "/b.mp3", duration: TimeSpan.FromMinutes(3));
        var repo = _fixture.CreatePlaylistRepository();

        await repo.CreateSmartPlaylist("Unordered", alice.Id, new SmartPlaylistRuleGroupDto(), 1, null, CancellationToken.None);
        await repo.CreateSmartPlaylist(
            "Unordered short",
            alice.Id,
            new SmartPlaylistRuleGroupDto { Rules = [new DurationRangeRuleDto { MaxDuration = TimeSpan.FromMinutes(5) }] },
            1,
            null,
            CancellationToken.None);

        (await repo.GetPlaylist("Unordered", alice.Id, CancellationToken.None))!.Tracks.Count.ShouldBe(2);
        (await repo.GetPlaylist("Unordered short", alice.Id, CancellationToken.None))!.Tracks.Count.ShouldBe(2);
    }
}
