using Plaat.Db.Models;
using Plaat.Db.Repositories;
using Shouldly;
using Xunit;

namespace Plaat.Db.Tests;

/// <summary>
/// Every smart playlist rule type and operator, evaluated in memory against one well-known track.
/// Unknown operator values (e.g. from a newer client) fall back to matching everything.
/// </summary>
public sealed class SmartPlaylistRulePredicateTests
{
    private const int UserId = 1;
    private const PlayCountPlaylistRuleType UnknownComparison = (PlayCountPlaylistRuleType)99;
    private const TextMatchType UnknownTextMatch = (TextMatchType)99;

    private static readonly DateTime Added = new(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Track SampleTrack(int? stars = 3, DateTime? lastPlayed = null)
    {
        return new Track
        {
            Title = "Hello World",
            FileHash = "h",
            MimeType = "audio/mpeg",
            PlayCount = 5,
            Year = 2000,
            BitRate = 320,
            Duration = TimeSpan.FromMinutes(3),
            AddedDate = Added,
            Album = new Album { Title = "Great Album" },
            Artists = [new Artist { Name = "The Band", NormalizedName = "THE BAND" }],
            Genres = [new Genre { Name = "Rock" }],
            Ratings = stars is { } s ? [new TrackRating { UserId = UserId, Stars = s }] : [],
            Plays = lastPlayed is { } played ? [new Play { DateTime = played }] : []
        };
    }

    private static bool Matches(SmartPlaylistRule rule, Track? track = null)
    {
        return PlaylistRepository.BuildRulePredicate(rule, UserId).Compile()(track ?? SampleTrack());
    }

    [Theory]
    [InlineData(PlayCountPlaylistRuleType.HigherOrEqual, 5u, true)]
    [InlineData(PlayCountPlaylistRuleType.HigherOrEqual, 6u, false)]
    [InlineData(PlayCountPlaylistRuleType.LowerOrEqual, 5u, true)]
    [InlineData(PlayCountPlaylistRuleType.LowerOrEqual, 4u, false)]
    [InlineData(PlayCountPlaylistRuleType.HigherThan, 4u, true)]
    [InlineData(PlayCountPlaylistRuleType.HigherThan, 5u, false)]
    [InlineData(PlayCountPlaylistRuleType.LowerThan, 6u, true)]
    [InlineData(PlayCountPlaylistRuleType.LowerThan, 5u, false)]
    [InlineData(UnknownComparison, 1000u, true)]
    public void PlayCountRule(PlayCountPlaylistRuleType type, uint playCount, bool expected)
    {
        Matches(new PlayCountPlaylistRule { PlayCountPlaylistRuleType = type, PlayCount = playCount }).ShouldBe(expected);
    }

    [Theory]
    [InlineData(PlayCountPlaylistRuleType.HigherOrEqual, 2000u, true)]
    [InlineData(PlayCountPlaylistRuleType.LowerOrEqual, 1999u, false)]
    [InlineData(PlayCountPlaylistRuleType.HigherThan, 2000u, false)]
    [InlineData(PlayCountPlaylistRuleType.LowerThan, 2001u, true)]
    [InlineData(UnknownComparison, 0u, true)]
    public void YearRule(PlayCountPlaylistRuleType type, uint year, bool expected)
    {
        Matches(new YearPlaylistRule { YearRuleMatchType = type, Year = year }).ShouldBe(expected);
    }

    [Theory]
    [InlineData(PlayCountPlaylistRuleType.HigherOrEqual, 320, true)]
    [InlineData(PlayCountPlaylistRuleType.LowerOrEqual, 319, false)]
    [InlineData(PlayCountPlaylistRuleType.HigherThan, 320, false)]
    [InlineData(PlayCountPlaylistRuleType.LowerThan, 321, true)]
    [InlineData(UnknownComparison, 0, true)]
    public void BitrateRule(PlayCountPlaylistRuleType type, int bitrate, bool expected)
    {
        Matches(new BitratePlaylistRule { BitrateRuleMatchType = type, Bitrate = bitrate }).ShouldBe(expected);
    }

    [Theory]
    [InlineData(PlayCountPlaylistRuleType.HigherOrEqual, 0, true)]
    [InlineData(PlayCountPlaylistRuleType.LowerOrEqual, -1, false)]
    [InlineData(PlayCountPlaylistRuleType.HigherThan, 0, false)]
    [InlineData(PlayCountPlaylistRuleType.LowerThan, 1, true)]
    [InlineData(UnknownComparison, 0, true)]
    public void AddedDateRule(PlayCountPlaylistRuleType type, int dayOffset, bool expected)
    {
        var rule = new AddedDateRule { DateRuleMatchType = type, Value = new DateTimeOffset(Added.AddDays(dayOffset)) };

        Matches(rule).ShouldBe(expected);
    }

    public static TheoryData<Func<TextMatchType, string, SmartPlaylistRule>, string> TextRules()
    {
        return new TheoryData<Func<TextMatchType, string, SmartPlaylistRule>, string>
        {
            { (type, value) => new TitlePlaylistRule { TextMatchType = type, Value = value }, "Hello World" },
            { (type, value) => new ArtistNamePlaylistRule { TextMatchType = type, Value = value }, "The Band" },
            { (type, value) => new AlbumNamePlaylistRule { TextMatchType = type, Value = value }, "Great Album" },
            { (type, value) => new GenrePlaylistRule { TextMatchType = type, Value = value }, "Rock" }
        };
    }

    [Theory]
    [MemberData(nameof(TextRules))]
    public void TextRules_EveryMatchType(Func<TextMatchType, string, SmartPlaylistRule> create, string fieldValue)
    {
        var middle = fieldValue[1..^1];

        Matches(create(TextMatchType.Contains, middle)).ShouldBeTrue();
        Matches(create(TextMatchType.Contains, "zzz")).ShouldBeFalse();
        Matches(create(TextMatchType.StartsWith, fieldValue[..2])).ShouldBeTrue();
        Matches(create(TextMatchType.StartsWith, middle)).ShouldBeFalse();
        Matches(create(TextMatchType.EndsWith, fieldValue[^2..])).ShouldBeTrue();
        Matches(create(TextMatchType.EndsWith, middle)).ShouldBeFalse();
        Matches(create(TextMatchType.Equals, fieldValue)).ShouldBeTrue();
        Matches(create(TextMatchType.Equals, middle)).ShouldBeFalse();
        Matches(create(UnknownTextMatch, "zzz")).ShouldBeTrue();
    }

    [Fact]
    public void AddedWithinDaysRule_ComparesAgainstNow()
    {
        var recent = SampleTrack();
        recent.AddedDate = DateTime.UtcNow.AddDays(-2);

        Matches(new AddedWithinDaysPlaylistRule { Days = 3 }, recent).ShouldBeTrue();
        Matches(new AddedWithinDaysPlaylistRule { Days = 1 }, recent).ShouldBeFalse();
    }

    [Fact]
    public void DayRules_WithAbsurdlyLargeDayCount_ClampInsteadOfOverflowing()
    {
        Matches(new AddedWithinDaysPlaylistRule { Days = uint.MaxValue }).ShouldBeTrue();
        Matches(new NotPlayedInDaysPlaylistRule { Days = uint.MaxValue }, SampleTrack(lastPlayed: DateTime.UtcNow)).ShouldBeFalse();
    }

    [Fact]
    public void NotPlayedInDaysRule_NeverPlayedOrPlayedLongAgoMatches()
    {
        Matches(new NotPlayedInDaysPlaylistRule { Days = 30 }, SampleTrack()).ShouldBeTrue();
        Matches(new NotPlayedInDaysPlaylistRule { Days = 30 }, SampleTrack(lastPlayed: DateTime.UtcNow.AddDays(-60))).ShouldBeTrue();
        Matches(new NotPlayedInDaysPlaylistRule { Days = 30 }, SampleTrack(lastPlayed: DateTime.UtcNow.AddDays(-1))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(2, null, true)]
    [InlineData(4, null, false)]
    [InlineData(null, 4, true)]
    [InlineData(null, 2, false)]
    [InlineData(2, 4, true)]
    [InlineData(4, 5, false)]
    public void DurationRangeRule(int? minMinutes, int? maxMinutes, bool expected)
    {
        var rule = new DurationRangePlaylistRule
        {
            MinDuration = minMinutes is { } min ? TimeSpan.FromMinutes(min) : null,
            MaxDuration = maxMinutes is { } max ? TimeSpan.FromMinutes(max) : null
        };

        Matches(rule).ShouldBe(expected);
    }

    [Theory]
    // rated 3 stars
    [InlineData(StarsPlaylistRuleType.Equals, 3, 3, true)]
    [InlineData(StarsPlaylistRuleType.Equals, 2, 3, false)]
    [InlineData(StarsPlaylistRuleType.Equals, 0, 3, false)]
    [InlineData(StarsPlaylistRuleType.HigherThan, 2, 3, true)]
    [InlineData(StarsPlaylistRuleType.HigherThan, 3, 3, false)]
    [InlineData(StarsPlaylistRuleType.HigherOrEqual, 3, 3, true)]
    [InlineData(StarsPlaylistRuleType.HigherOrEqual, 0, 3, true)]
    [InlineData(StarsPlaylistRuleType.LowerThan, 4, 3, true)]
    [InlineData(StarsPlaylistRuleType.LowerThan, 3, 3, false)]
    [InlineData(StarsPlaylistRuleType.LowerThan, 0, 3, false)]
    [InlineData(StarsPlaylistRuleType.LowerOrEqual, 3, 3, true)]
    [InlineData(StarsPlaylistRuleType.LowerOrEqual, 2, 3, false)]
    [InlineData((StarsPlaylistRuleType)99, 5, 3, true)]
    // never rated counts as 0 stars
    [InlineData(StarsPlaylistRuleType.Equals, 0, null, true)]
    [InlineData(StarsPlaylistRuleType.HigherOrEqual, 1, null, false)]
    [InlineData(StarsPlaylistRuleType.HigherOrEqual, 0, null, true)]
    [InlineData(StarsPlaylistRuleType.LowerThan, 1, null, true)]
    [InlineData(StarsPlaylistRuleType.LowerThan, 0, null, false)]
    [InlineData(StarsPlaylistRuleType.LowerOrEqual, 0, null, true)]
    public void StarsRule(StarsPlaylistRuleType type, int stars, int? rated, bool expected)
    {
        Matches(new StarsPlaylistRule { StarsRuleMatchType = type, Stars = stars }, SampleTrack(rated)).ShouldBe(expected);
    }

    [Fact]
    public void StarsRule_OnlyConsidersTheGivenUsersRating()
    {
        var track = SampleTrack(stars: null);
        track.Ratings.Add(new TrackRating { UserId = UserId + 1, Stars = 5 });

        Matches(new StarsPlaylistRule { StarsRuleMatchType = StarsPlaylistRuleType.Equals, Stars = 5 }, track).ShouldBeFalse();
    }

    [Fact]
    public void UnknownRuleType_Throws()
    {
        Should.Throw<InvalidOperationException>(() => PlaylistRepository.BuildRulePredicate(new UnknownRule(), UserId));
    }

    private sealed class UnknownRule : SmartPlaylistRule;
}
