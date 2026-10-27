using System.Globalization;
using Bunit;
using Plaat.Contract.Models;
using Plaat.Frontend.Components.Playlists;
using Shouldly;
using Xunit;

namespace Plaat.Frontend.Tests.Components;

/// <summary>
/// Editing a smart playlist's rule tree: every rule type's inputs write through to the model, and
/// groups can be combined, nested (up to the maximum depth) and removed.
/// </summary>
public sealed class SmartRuleGroupEditorComponentTests : BunitContext
{
    private IRenderedComponent<SmartRuleGroupEditor> RenderEditor(SmartRuleGroupUiModel group, int depth = 0, bool canRemoveSelf = false, Action? onRemove = null)
    {
        return Render<SmartRuleGroupEditor>(p => p
            .Add(x => x.Group, group)
            .Add(x => x.Depth, depth)
            .Add(x => x.CanRemoveSelf, canRemoveSelf)
            .Add(x => x.OnRemoveSelf, onRemove ?? (() => { })));
    }

    private static SmartRuleGroupUiModel WithRule(SmartRuleType type)
    {
        return new SmartRuleGroupUiModel { Rules = [new SmartRuleUiModel { Type = type }] };
    }

    [Fact]
    public void CombinatorButtons_ToggleBetweenAndAndOr()
    {
        var group = new SmartRuleGroupUiModel();
        var cut = RenderEditor(group);

        cut.FindAll(".combinator-btn")[1].Click();
        group.Combinator.ShouldBe(SmartPlaylistGroupCombinator.Or);
        cut.FindAll(".combinator-btn")[1].ClassList.ShouldContain("active");

        cut.FindAll(".combinator-btn")[0].Click();
        group.Combinator.ShouldBe(SmartPlaylistGroupCombinator.And);
    }

    [Fact]
    public void AddAndRemoveRules()
    {
        var group = new SmartRuleGroupUiModel();
        var cut = RenderEditor(group);

        cut.Find(".add-rule-btn").Click();
        cut.Find(".add-rule-btn").Click();
        group.Rules.Count.ShouldBe(2);
        cut.FindAll(".rule-row").Count.ShouldBe(2);

        cut.Find(".rule-row .remove-rule-btn").Click();
        group.Rules.Count.ShouldBe(1);
    }

    [Fact]
    public void ChildGroups_CanBeAddedUntilTheMaximumDepthAndRemoved()
    {
        var group = new SmartRuleGroupUiModel();
        var cut = RenderEditor(group);

        cut.FindAll(".add-rule-btn")[1].Click();
        var child = group.ChildGroups.ShouldHaveSingleItem();
        cut.FindAll(".rule-group-nested").ShouldHaveSingleItem();

        cut.Find(".rule-group-nested .rule-group-header .remove-rule-btn").Click();
        group.ChildGroups.ShouldNotContain(child);

        RenderEditor(new SmartRuleGroupUiModel(), depth: SmartRuleGroupEditor.MaxDepth - 1)
            .FindAll(".add-rule-btn").ShouldHaveSingleItem();
    }

    [Fact]
    public void TopLevelGroup_HasNoRemoveButton_ButNestedGroupsNotifyTheirParent()
    {
        RenderEditor(new SmartRuleGroupUiModel()).FindAll(".rule-group-header .remove-rule-btn").ShouldBeEmpty();

        var removed = false;
        var cut = RenderEditor(new SmartRuleGroupUiModel(), depth: 1, canRemoveSelf: true, onRemove: () => removed = true);
        cut.Find(".rule-group-header .remove-rule-btn").Click();

        removed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Title")]
    [InlineData("Stars")]
    [InlineData("DurationRange")]
    public void RuleType_CanBeChanged(string type)
    {
        var group = WithRule(SmartRuleType.PlayCount);
        var cut = RenderEditor(group);

        cut.Find(".rule-select").Change(type);

        group.Rules.Single().Type.ShouldBe(Enum.Parse<SmartRuleType>(type));
    }

    [Fact]
    public void InvalidSelections_AreIgnored()
    {
        var group = WithRule(SmartRuleType.PlayCount);
        var cut = RenderEditor(group);

        cut.Find(".rule-select").Change("NotAType");
        cut.Find(".rule-select-op").Change("NotAnOperator");
        cut.Find(".rule-input-number").Change("-1");

        var rule = group.Rules.Single();
        rule.Type.ShouldBe(SmartRuleType.PlayCount);
        rule.PlayCountType.ShouldBe(PlayCountPlaylistRuleType.HigherThan);
        rule.PlayCountValue.ShouldBe(0u);
    }

    [Fact]
    public void PlayCountRule_EditsOperatorAndValue()
    {
        var group = WithRule(SmartRuleType.PlayCount);
        var cut = RenderEditor(group);

        cut.Find(".rule-select-op").Change("LowerOrEqual");
        cut.Find(".rule-input-number").Change("7");

        group.Rules.Single().PlayCountType.ShouldBe(PlayCountPlaylistRuleType.LowerOrEqual);
        group.Rules.Single().PlayCountValue.ShouldBe(7u);
    }

    [Theory]
    [InlineData(SmartRuleType.Title)]
    [InlineData(SmartRuleType.ArtistName)]
    [InlineData(SmartRuleType.AlbumName)]
    [InlineData(SmartRuleType.Genre)]
    public void TextRules_EditMatchTypeAndValue(SmartRuleType type)
    {
        var group = WithRule(type);
        var cut = RenderEditor(group);

        cut.Find(".rule-select-op").Change("EndsWith");
        cut.Find("input[type=text]").Input("abc");

        group.Rules.Single().TextMatchType.ShouldBe(TextMatchType.EndsWith);
        group.Rules.Single().TextValue.ShouldBe("abc");

        cut.Find("input[type=text]").Input((string?)null);
        group.Rules.Single().TextValue.ShouldBe("");
    }

    [Fact]
    public void AddedDateRule_EditsComparisonAndDate()
    {
        var group = WithRule(SmartRuleType.AddedDate);
        var cut = RenderEditor(group);

        cut.Find(".rule-select-op").Change("LowerThan");
        cut.Find("input[type=date]").Change("2021-06-15");
        cut.Find("input[type=date]").Change("not a date");

        group.Rules.Single().DateRuleType.ShouldBe(DateComparisonType.LowerThan);
        group.Rules.Single().DateValue.Date.ShouldBe(new DateTime(2021, 6, 15));
    }

    [Theory]
    [InlineData(SmartRuleType.AddedWithinDays)]
    [InlineData(SmartRuleType.NotPlayedInDays)]
    public void DayRules_EditTheDayCount(SmartRuleType type)
    {
        var group = WithRule(type);
        var cut = RenderEditor(group);

        cut.Find(".rule-input-number").Change("14");
        cut.Find(".rule-input-number").Change("x");

        group.Rules.Single().DaysValue.ShouldBe(14u);
    }

    [Fact]
    public void DurationRule_EditsMinAndMaxMinutesAndClearsNonPositiveValues()
    {
        var group = WithRule(SmartRuleType.DurationRange);
        var cut = RenderEditor(group);

        cut.FindAll(".rule-input-number")[0].Change("2.5");
        cut.FindAll(".rule-input-number")[1].Change("10");

        group.Rules.Single().MinDuration.ShouldBe(TimeSpan.FromMinutes(2.5));
        group.Rules.Single().MaxDuration.ShouldBe(TimeSpan.FromMinutes(10));
        cut.FindAll(".rule-input-number")[0].GetAttribute("value").ShouldBe("2.5");

        cut.FindAll(".rule-input-number")[0].Change("0");
        cut.FindAll(".rule-input-number")[1].Change("");

        group.Rules.Single().MinDuration.ShouldBeNull();
        group.Rules.Single().MaxDuration.ShouldBeNull();
    }

    [Fact]
    public void DurationRule_OnAServerWithACommaDecimalCulture_StillReadsTheBrowsersDotDecimals()
    {
        var original = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            var group = WithRule(SmartRuleType.DurationRange);
            group.Rules.Single().MaxDuration = TimeSpan.FromMinutes(1.5);
            var cut = RenderEditor(group);

            cut.FindAll(".rule-input-number")[1].GetAttribute("value").ShouldBe("1.5");

            cut.FindAll(".rule-input-number")[0].Change("2.5");

            group.Rules.Single().MinDuration.ShouldBe(TimeSpan.FromMinutes(2.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void YearRule_EditsComparisonAndYear()
    {
        var group = WithRule(SmartRuleType.Year);
        var cut = RenderEditor(group);

        cut.Find(".rule-select-op").Change("LowerThan");
        cut.Find(".rule-input-number").Change("1980");
        cut.Find(".rule-input-number").Change("abc");

        group.Rules.Single().YearRuleType.ShouldBe(PlayCountPlaylistRuleType.LowerThan);
        group.Rules.Single().YearValue.ShouldBe(1980u);
    }

    [Fact]
    public void BitrateRule_EditsComparisonAndBitrate()
    {
        var group = WithRule(SmartRuleType.Bitrate);
        var cut = RenderEditor(group);

        cut.Find(".rule-select-op").Change("HigherOrEqual");
        cut.Find(".rule-input-number").Change("256");
        cut.Find(".rule-input-number").Change("fast");

        group.Rules.Single().BitrateRuleType.ShouldBe(PlayCountPlaylistRuleType.HigherOrEqual);
        group.Rules.Single().BitrateValue.ShouldBe(256);
        cut.Markup.ShouldContain("kbps");
    }

    [Fact]
    public void StarsRule_EditsComparisonAndClampsStars()
    {
        var group = WithRule(SmartRuleType.Stars);
        var cut = RenderEditor(group);

        cut.FindAll(".rule-select-op")[0].Change("LowerThan");
        cut.FindAll(".rule-select-op")[0].Change("bogus");
        cut.FindAll(".rule-select-op")[1].Change("9");
        group.Rules.Single().StarsValue.ShouldBe(TrackRatingDto.MaxStars);
        cut.FindAll(".rule-select-op")[1].Change("2");
        cut.FindAll(".rule-select-op")[1].Change("x");

        group.Rules.Single().StarsRuleType.ShouldBe(StarsComparisonType.LowerThan);
        group.Rules.Single().StarsValue.ShouldBe(2);
        cut.FindAll(".rule-select-op")[1].QuerySelectorAll("option").Length.ShouldBe(TrackRatingDto.MaxStars + 1);
    }

    [Fact]
    public void OperatorSelects_IgnoreUnknownValuesForEveryRuleType()
    {
        foreach (var type in new[] { SmartRuleType.Title, SmartRuleType.AddedDate, SmartRuleType.Year, SmartRuleType.Bitrate })
        {
            var group = WithRule(type);
            var before = group.Rules.Single();
            var cut = RenderEditor(group);

            cut.Find(".rule-select-op").Change("bogus");

            before.TextMatchType.ShouldBe(TextMatchType.Contains);
            before.DateRuleType.ShouldBe(DateComparisonType.HigherOrEqual);
            before.YearRuleType.ShouldBe(PlayCountPlaylistRuleType.HigherOrEqual);
            before.BitrateRuleType.ShouldBe(PlayCountPlaylistRuleType.HigherOrEqual);
        }
    }
}
