using System.Text.Json;
using Plaat.Contract.Models;
using Shouldly;
using Xunit;

namespace Plaat.Contract.Tests;

public sealed class SmartPlaylistRuleGroupDtoTests
{
    [Fact]
    public void SmartPlaylistRuleGroupDto_RoundTrips_EveryRuleTypeAndNestedChildGroup()
    {
        var group = new SmartPlaylistRuleGroupDto
        {
            Combinator = SmartPlaylistGroupCombinator.Or,
            Rules =
            [
                new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 5 },
                new TitlePlaylistRuleDto { TextMatchType = TextMatchType.Contains, Value = "Song" },
                new ArtistNamePlaylistRuleDto { TextMatchType = TextMatchType.Equals, Value = "Artist" },
                new AlbumNamePlaylistRuleDto { TextMatchType = TextMatchType.StartsWith, Value = "Alb" },
                new GenrePlaylistRuleDto { TextMatchType = TextMatchType.EndsWith, Value = "ock" },
                new AddedDateRuleDto { RuleType = DateComparisonType.HigherOrEqual, Value = DateTimeOffset.UtcNow },
                new AddedWithinDaysRuleDto { Days = 30 },
                new NotPlayedInDaysRuleDto { Days = 60 },
                new DurationRangeRuleDto { MinDuration = TimeSpan.FromMinutes(2), MaxDuration = TimeSpan.FromMinutes(6) },
                new YearRuleDto { RuleType = PlayCountPlaylistRuleType.HigherOrEqual, Year = 2000 },
                new BitrateRuleDto { RuleType = PlayCountPlaylistRuleType.LowerThan, Bitrate = 192 },
                new StarsRuleDto { RuleType = StarsComparisonType.Equals, Stars = 4 }
            ],
            ChildGroups =
            [
                new SmartPlaylistRuleGroupDto
                {
                    Combinator = SmartPlaylistGroupCombinator.And,
                    Rules = [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.LowerOrEqual, PlayCount = 1 }]
                }
            ]
        };

        var json = JsonSerializer.Serialize(group);
        var deserialized = JsonSerializer.Deserialize<SmartPlaylistRuleGroupDto>(json);

        deserialized.ShouldNotBeNull();
        deserialized.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        deserialized.Rules.Count.ShouldBe(group.Rules.Count);

        deserialized.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCount.ShouldBe(5u);
        deserialized.Rules.OfType<TitlePlaylistRuleDto>().Single().Value.ShouldBe("Song");
        deserialized.Rules.OfType<ArtistNamePlaylistRuleDto>().Single().Value.ShouldBe("Artist");
        deserialized.Rules.OfType<AlbumNamePlaylistRuleDto>().Single().Value.ShouldBe("Alb");
        deserialized.Rules.OfType<GenrePlaylistRuleDto>().Single().Value.ShouldBe("ock");
        deserialized.Rules.OfType<AddedDateRuleDto>().ShouldHaveSingleItem();
        deserialized.Rules.OfType<AddedWithinDaysRuleDto>().Single().Days.ShouldBe(30u);
        deserialized.Rules.OfType<NotPlayedInDaysRuleDto>().Single().Days.ShouldBe(60u);
        var durationRule = deserialized.Rules.OfType<DurationRangeRuleDto>().Single();
        durationRule.MinDuration.ShouldBe(TimeSpan.FromMinutes(2));
        durationRule.MaxDuration.ShouldBe(TimeSpan.FromMinutes(6));
        deserialized.Rules.OfType<YearRuleDto>().Single().Year.ShouldBe(2000u);
        deserialized.Rules.OfType<BitrateRuleDto>().Single().Bitrate.ShouldBe(192);
        var starsRule = deserialized.Rules.OfType<StarsRuleDto>().Single();
        starsRule.RuleType.ShouldBe(StarsComparisonType.Equals);
        starsRule.Stars.ShouldBe(4);

        deserialized.ChildGroups.ShouldHaveSingleItem();
        var childGroup = deserialized.ChildGroups.Single();
        childGroup.Combinator.ShouldBe(SmartPlaylistGroupCombinator.And);
        childGroup.Rules.OfType<PlayCountPlaylistRuleDto>().Single().PlayCount.ShouldBe(1u);
    }

    [Fact]
    public void CreateSmartPlaylistRequest_RoundTrips_WithLimitSet()
    {
        var request = new CreateSmartPlaylistRequest
        {
            RootGroup = new SmartPlaylistRuleGroupDto
            {
                Rules = [new PlayCountPlaylistRuleDto { PlayCountPlaylistRuleType = PlayCountPlaylistRuleType.HigherThan, PlayCount = 1 }]
            },
            Limit = 25,
            LimitOrderBy = SmartPlaylistLimitOrderByDto.RecentlyAdded
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<CreateSmartPlaylistRequest>(json);

        deserialized.ShouldNotBeNull();
        deserialized.Limit.ShouldBe(25);
        deserialized.LimitOrderBy.ShouldBe(SmartPlaylistLimitOrderByDto.RecentlyAdded);
        deserialized.RootGroup.Rules.ShouldHaveSingleItem();
    }

    [Fact]
    public void CreateSmartPlaylistRequest_RoundTrips_WithLimitUnset()
    {
        var request = new CreateSmartPlaylistRequest
        {
            RootGroup = new SmartPlaylistRuleGroupDto(),
            Limit = null,
            LimitOrderBy = null
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<CreateSmartPlaylistRequest>(json);

        deserialized.ShouldNotBeNull();
        deserialized.Limit.ShouldBeNull();
        deserialized.LimitOrderBy.ShouldBeNull();
    }
}
