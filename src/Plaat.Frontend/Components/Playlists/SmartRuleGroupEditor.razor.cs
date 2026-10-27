using System.Globalization;
using Microsoft.AspNetCore.Components;
using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Playlists;

public partial class SmartRuleGroupEditor : ComponentBase
{
    public const int MaxDepth = SmartPlaylistRuleGroupLimits.MaxGroupDepth;

    [Parameter, EditorRequired]
    public required SmartRuleGroupUiModel Group { get; set; }

    [Parameter]
    public int Depth { get; set; }

    [Parameter]
    public bool CanRemoveSelf { get; set; }

    [Parameter]
    public EventCallback OnRemoveSelf { get; set; }

    private bool CanAddChildGroup
    {
        get
        {
            return Depth + 1 < MaxDepth;
        }
    }

    private void ToggleCombinator()
    {
        Group.Combinator = Group.Combinator == SmartPlaylistGroupCombinator.And
            ? SmartPlaylistGroupCombinator.Or
            : SmartPlaylistGroupCombinator.And;
    }

    private void AddRule()
    {
        Group.Rules.Add(new SmartRuleUiModel());
    }

    private void RemoveRule(SmartRuleUiModel rule)
    {
        Group.Rules.Remove(rule);
    }

    private void AddChildGroup()
    {
        Group.ChildGroups.Add(new SmartRuleGroupUiModel());
    }

    private void RemoveChildGroup(SmartRuleGroupUiModel child)
    {
        Group.ChildGroups.Remove(child);
    }

    private static void OnRuleTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<SmartRuleType>(e.Value?.ToString(), out var type))
        {
            rule.Type = type;
        }
    }

    private static void OnPlayCountTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<PlayCountPlaylistRuleType>(e.Value?.ToString(), out var type))
        {
            rule.PlayCountType = type;
        }
    }

    private static void OnPlayCountValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (uint.TryParse(e.Value?.ToString(), out var val))
        {
            rule.PlayCountValue = val;
        }
    }

    private static void OnTextMatchTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<TextMatchType>(e.Value?.ToString(), out var type))
        {
            rule.TextMatchType = type;
        }
    }

    private static void OnTextValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        rule.TextValue = e.Value?.ToString() ?? "";
    }

    private static void OnDateRuleTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<DateComparisonType>(e.Value?.ToString(), out var type))
        {
            rule.DateRuleType = type;
        }
    }

    private static void OnDateValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (DateTimeOffset.TryParse(e.Value?.ToString(), out var val))
        {
            rule.DateValue = val;
        }
    }

    private static void OnDaysValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (uint.TryParse(e.Value?.ToString(), out var val))
        {
            rule.DaysValue = val;
        }
    }

    private static void OnMinDurationChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        rule.MinDuration = TryParseMinutes(e.Value?.ToString());
    }

    private static void OnMaxDurationChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        rule.MaxDuration = TryParseMinutes(e.Value?.ToString());
    }

    // An <input type="number"> always sends and expects a "." decimal separator, whatever the
    // server's culture - under e.g. nl-NL a culture-sensitive parse reads "2.5" as 25.
    private static TimeSpan? TryParseMinutes(string? text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var minutes) && minutes > 0
            ? TimeSpan.FromMinutes(minutes)
            : null;
    }

    private static string? FormatMinutes(TimeSpan? duration)
    {
        return duration?.TotalMinutes.ToString(CultureInfo.InvariantCulture);
    }

    private static void OnYearRuleTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<PlayCountPlaylistRuleType>(e.Value?.ToString(), out var type))
        {
            rule.YearRuleType = type;
        }
    }

    private static void OnYearValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (uint.TryParse(e.Value?.ToString(), out var val))
        {
            rule.YearValue = val;
        }
    }

    private static void OnBitrateRuleTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<PlayCountPlaylistRuleType>(e.Value?.ToString(), out var type))
        {
            rule.BitrateRuleType = type;
        }
    }

    private static void OnBitrateValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val))
        {
            rule.BitrateValue = val;
        }
    }

    private static void OnStarsRuleTypeChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (Enum.TryParse<StarsComparisonType>(e.Value?.ToString(), out var type))
        {
            rule.StarsRuleType = type;
        }
    }

    private static void OnStarsValueChanged(SmartRuleUiModel rule, ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var val))
        {
            rule.StarsValue = Math.Clamp(val, TrackRatingDto.MinStars, TrackRatingDto.MaxStars);
        }
    }
}
