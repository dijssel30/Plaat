using Plaat.Contract.Models;
using Plaat.Frontend.Components.Playlists;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests;

public sealed class SmartRuleMapperTests
{
    private static readonly DateTimeOffset SomeDate = new(2022, 3, 4, 0, 0, 0, TimeSpan.Zero);

    private static SmartRuleUiModel[] AllRules()
    {
        return
        [
            new SmartRuleUiModel { Type = SmartRuleType.PlayCount, PlayCountType = PlayCountPlaylistRuleType.LowerThan, PlayCountValue = 4 },
            new SmartRuleUiModel { Type = SmartRuleType.Title, TextMatchType = TextMatchType.StartsWith, TextValue = "Ti" },
            new SmartRuleUiModel { Type = SmartRuleType.ArtistName, TextMatchType = TextMatchType.Equals, TextValue = "Ar" },
            new SmartRuleUiModel { Type = SmartRuleType.AlbumName, TextMatchType = TextMatchType.EndsWith, TextValue = "Al" },
            new SmartRuleUiModel { Type = SmartRuleType.Genre, TextMatchType = TextMatchType.Contains, TextValue = "Ge" },
            new SmartRuleUiModel { Type = SmartRuleType.AddedDate, DateRuleType = DateComparisonType.LowerThan, DateValue = SomeDate },
            new SmartRuleUiModel { Type = SmartRuleType.AddedWithinDays, DaysValue = 7 },
            new SmartRuleUiModel { Type = SmartRuleType.NotPlayedInDays, DaysValue = 90 },
            new SmartRuleUiModel { Type = SmartRuleType.DurationRange, MinDuration = TimeSpan.FromMinutes(1), MaxDuration = TimeSpan.FromMinutes(2) },
            new SmartRuleUiModel { Type = SmartRuleType.Year, YearRuleType = PlayCountPlaylistRuleType.HigherThan, YearValue = 1999 },
            new SmartRuleUiModel { Type = SmartRuleType.Bitrate, BitrateRuleType = PlayCountPlaylistRuleType.LowerOrEqual, BitrateValue = 192 },
            new SmartRuleUiModel { Type = SmartRuleType.Stars, StarsRuleType = StarsComparisonType.Equals, StarsValue = 2 }
        ];
    }

    public static TheoryData<SmartRuleUiModel> EveryRuleType()
    {
        return [.. AllRules()];
    }

    [Theory]
    [MemberData(nameof(EveryRuleType))]
    public void EveryRuleType_RoundTripsThroughTheDto(SmartRuleUiModel rule)
    {
        var group = new SmartRuleGroupUiModel { Combinator = SmartPlaylistGroupCombinator.Or, Rules = [rule] };

        var roundTripped = SmartRuleMapper.FromDto(SmartRuleMapper.ToDto(group));

        roundTripped.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        var back = roundTripped.Rules.ShouldHaveSingleItem();
        back.Type.ShouldBe(rule.Type);
        back.PlayCountType.ShouldBe(rule.PlayCountType);
        back.PlayCountValue.ShouldBe(rule.PlayCountValue);
        back.TextMatchType.ShouldBe(rule.TextMatchType);
        back.TextValue.ShouldBe(rule.TextValue);
        back.DaysValue.ShouldBe(rule.DaysValue);
        back.MinDuration.ShouldBe(rule.MinDuration);
        back.MaxDuration.ShouldBe(rule.MaxDuration);
        back.YearRuleType.ShouldBe(rule.YearRuleType);
        back.YearValue.ShouldBe(rule.YearValue);
        back.BitrateRuleType.ShouldBe(rule.BitrateRuleType);
        back.BitrateValue.ShouldBe(rule.BitrateValue);
        back.StarsRuleType.ShouldBe(rule.StarsRuleType);
        back.StarsValue.ShouldBe(rule.StarsValue);
        if (rule.Type == SmartRuleType.AddedDate)
        {
            back.DateRuleType.ShouldBe(rule.DateRuleType);
            back.DateValue.ShouldBe(rule.DateValue);
        }
    }

    [Fact]
    public void ToDto_MapsEachRuleToItsDtoType()
    {
        var group = new SmartRuleGroupUiModel { Rules = [.. AllRules()] };

        var dto = SmartRuleMapper.ToDto(group);

        dto.Rules.Select(r => r.GetType()).ShouldBe([
            typeof(PlayCountPlaylistRuleDto), typeof(TitlePlaylistRuleDto), typeof(ArtistNamePlaylistRuleDto),
            typeof(AlbumNamePlaylistRuleDto), typeof(GenrePlaylistRuleDto), typeof(AddedDateRuleDto),
            typeof(AddedWithinDaysRuleDto), typeof(NotPlayedInDaysRuleDto), typeof(DurationRangeRuleDto),
            typeof(YearRuleDto), typeof(BitrateRuleDto), typeof(StarsRuleDto)
        ]);
    }

    [Fact]
    public void NestedGroups_RoundTrip()
    {
        var group = new SmartRuleGroupUiModel
        {
            ChildGroups =
            [
                new SmartRuleGroupUiModel
                {
                    Combinator = SmartPlaylistGroupCombinator.Or,
                    Rules = [new SmartRuleUiModel { Type = SmartRuleType.Year, YearValue = 2001 }]
                }
            ]
        };

        var back = SmartRuleMapper.FromDto(SmartRuleMapper.ToDto(group));

        back.Rules.ShouldBeEmpty();
        var child = back.ChildGroups.ShouldHaveSingleItem();
        child.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        child.Rules.Single().YearValue.ShouldBe(2001u);
    }

    [Fact]
    public void UnknownRuleType_Throws()
    {
        var group = new SmartRuleGroupUiModel { Rules = [new SmartRuleUiModel { Type = (SmartRuleType)99 }] };

        Should.Throw<InvalidOperationException>(() => SmartRuleMapper.ToDto(group));
    }

    [Fact]
    public void UnknownRuleDtoType_Throws()
    {
        var dto = new SmartPlaylistRuleGroupDto { Rules = [new UnknownRuleDto()] };

        Should.Throw<InvalidOperationException>(() => SmartRuleMapper.FromDto(dto));
    }

    [Fact]
    public void HasAnyRule_LooksThroughChildGroups()
    {
        SmartRuleMapper.HasAnyRule(new SmartRuleGroupUiModel()).ShouldBeFalse();
        SmartRuleMapper.HasAnyRule(new SmartRuleGroupUiModel { ChildGroups = [new SmartRuleGroupUiModel()] }).ShouldBeFalse();
        SmartRuleMapper.HasAnyRule(new SmartRuleGroupUiModel { Rules = [new SmartRuleUiModel()] }).ShouldBeTrue();
        SmartRuleMapper.HasAnyRule(new SmartRuleGroupUiModel
        {
            ChildGroups = [new SmartRuleGroupUiModel { Rules = [new SmartRuleUiModel()] }]
        }).ShouldBeTrue();
    }

    [Fact]
    public void HasInvalidDurationRangeRule_FlagsARangeWithNeitherBoundAnywhereInTheTree()
    {
        var emptyRange = new SmartRuleUiModel { Type = SmartRuleType.DurationRange };
        var minOnly = new SmartRuleUiModel { Type = SmartRuleType.DurationRange, MinDuration = TimeSpan.FromMinutes(1) };
        var maxOnly = new SmartRuleUiModel { Type = SmartRuleType.DurationRange, MaxDuration = TimeSpan.FromMinutes(1) };
        var otherRule = new SmartRuleUiModel { Type = SmartRuleType.Title };

        SmartRuleMapper.HasInvalidDurationRangeRule(new SmartRuleGroupUiModel { Rules = [emptyRange] }).ShouldBeTrue();
        SmartRuleMapper.HasInvalidDurationRangeRule(new SmartRuleGroupUiModel { Rules = [minOnly, maxOnly, otherRule] }).ShouldBeFalse();
        SmartRuleMapper.HasInvalidDurationRangeRule(new SmartRuleGroupUiModel
        {
            Rules = [minOnly],
            ChildGroups = [new SmartRuleGroupUiModel { Rules = [emptyRange] }]
        }).ShouldBeTrue();
    }

    private sealed class UnknownRuleDto : SmartPlaylistRuleDto;
}
