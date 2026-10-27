using Plaat.Contract.Models;

namespace Plaat.Frontend.Components.Playlists;

public enum SmartRuleType
{
    PlayCount,
    Title,
    ArtistName,
    AlbumName,
    Genre,
    AddedDate,
    AddedWithinDays,
    NotPlayedInDays,
    DurationRange,
    Year,
    Bitrate,
    Stars
}

public sealed class SmartRuleUiModel
{
    public SmartRuleType Type { get; set; } = SmartRuleType.PlayCount;
    public PlayCountPlaylistRuleType PlayCountType { get; set; } = PlayCountPlaylistRuleType.HigherThan;
    public uint PlayCountValue { get; set; }
    public TextMatchType TextMatchType { get; set; } = TextMatchType.Contains;
    public string TextValue { get; set; } = "";
    public DateComparisonType DateRuleType { get; set; } = DateComparisonType.HigherOrEqual;
    public DateTimeOffset DateValue { get; set; } = DateTimeOffset.Now.Date;
    public uint DaysValue { get; set; } = 30;
    public TimeSpan? MinDuration { get; set; }
    public TimeSpan? MaxDuration { get; set; }
    public PlayCountPlaylistRuleType YearRuleType { get; set; } = PlayCountPlaylistRuleType.HigherOrEqual;
    public uint YearValue { get; set; } = (uint)DateTime.Now.Year;
    public PlayCountPlaylistRuleType BitrateRuleType { get; set; } = PlayCountPlaylistRuleType.HigherOrEqual;
    public int BitrateValue { get; set; }
    public StarsComparisonType StarsRuleType { get; set; } = StarsComparisonType.HigherOrEqual;
    public int StarsValue { get; set; } = TrackRatingDto.MaxStars;
}

public sealed class SmartRuleGroupUiModel
{
    public SmartPlaylistGroupCombinator Combinator { get; set; } = SmartPlaylistGroupCombinator.And;
    public List<SmartRuleUiModel> Rules { get; set; } = [];
    public List<SmartRuleGroupUiModel> ChildGroups { get; set; } = [];
}

/// <summary>
/// Maps between the UI-editable rule-group tree and its wire DTO shape, in both directions, plus
/// client-side validation shared by the create and edit flows.
/// </summary>
public static class SmartRuleMapper
{
    public static bool HasAnyRule(SmartRuleGroupUiModel group)
    {
        return group.Rules.Count > 0 || group.ChildGroups.Any(HasAnyRule);
    }

    public static bool HasInvalidDurationRangeRule(SmartRuleGroupUiModel group)
    {
        return group.Rules.Any(r => r.Type == SmartRuleType.DurationRange && r.MinDuration is null && r.MaxDuration is null)
        || group.ChildGroups.Any(HasInvalidDurationRangeRule);
    }

    public static SmartPlaylistRuleGroupDto ToDto(SmartRuleGroupUiModel group)
    {
        return new()
        {
            Combinator = group.Combinator,
            Rules = [.. group.Rules.Select(ToDto)],
            ChildGroups = [.. group.ChildGroups.Select(ToDto)]
        };
    }

    private static SmartPlaylistRuleDto ToDto(SmartRuleUiModel r)
    {
        return r.Type switch
        {
            SmartRuleType.PlayCount => new PlayCountPlaylistRuleDto
            {
                PlayCountPlaylistRuleType = r.PlayCountType,
                PlayCount = r.PlayCountValue
            },
            SmartRuleType.Title => new TitlePlaylistRuleDto
            {
                TextMatchType = r.TextMatchType,
                Value = r.TextValue
            },
            SmartRuleType.ArtistName => new ArtistNamePlaylistRuleDto
            {
                TextMatchType = r.TextMatchType,
                Value = r.TextValue
            },
            SmartRuleType.AlbumName => new AlbumNamePlaylistRuleDto
            {
                TextMatchType = r.TextMatchType,
                Value = r.TextValue
            },
            SmartRuleType.Genre => new GenrePlaylistRuleDto
            {
                TextMatchType = r.TextMatchType,
                Value = r.TextValue
            },
            SmartRuleType.AddedDate => new AddedDateRuleDto
            {
                RuleType = r.DateRuleType,
                Value = r.DateValue
            },
            SmartRuleType.AddedWithinDays => new AddedWithinDaysRuleDto
            {
                Days = r.DaysValue
            },
            SmartRuleType.NotPlayedInDays => new NotPlayedInDaysRuleDto
            {
                Days = r.DaysValue
            },
            SmartRuleType.DurationRange => new DurationRangeRuleDto
            {
                MinDuration = r.MinDuration,
                MaxDuration = r.MaxDuration
            },
            SmartRuleType.Year => new YearRuleDto
            {
                RuleType = r.YearRuleType,
                Year = r.YearValue
            },
            SmartRuleType.Bitrate => new BitrateRuleDto
            {
                RuleType = r.BitrateRuleType,
                Bitrate = r.BitrateValue
            },
            SmartRuleType.Stars => new StarsRuleDto
            {
                RuleType = r.StarsRuleType,
                Stars = r.StarsValue
            },
            _ => throw new InvalidOperationException("Unknown rule type")
        };
    }

    public static SmartRuleGroupUiModel FromDto(SmartPlaylistRuleGroupDto dto)
    {
        return new()
        {
            Combinator = dto.Combinator,
            Rules = [.. dto.Rules.Select(FromDto)],
            ChildGroups = [.. dto.ChildGroups.Select(FromDto)]
        };
    }

    private static SmartRuleUiModel FromDto(SmartPlaylistRuleDto dto)
    {
        return dto switch
        {
            PlayCountPlaylistRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.PlayCount,
                PlayCountType = r.PlayCountPlaylistRuleType,
                PlayCountValue = r.PlayCount
            },
            TitlePlaylistRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.Title,
                TextMatchType = r.TextMatchType,
                TextValue = r.Value
            },
            ArtistNamePlaylistRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.ArtistName,
                TextMatchType = r.TextMatchType,
                TextValue = r.Value
            },
            AlbumNamePlaylistRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.AlbumName,
                TextMatchType = r.TextMatchType,
                TextValue = r.Value
            },
            GenrePlaylistRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.Genre,
                TextMatchType = r.TextMatchType,
                TextValue = r.Value
            },
            AddedDateRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.AddedDate,
                DateRuleType = r.RuleType,
                DateValue = r.Value
            },
            AddedWithinDaysRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.AddedWithinDays,
                DaysValue = r.Days
            },
            NotPlayedInDaysRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.NotPlayedInDays,
                DaysValue = r.Days
            },
            DurationRangeRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.DurationRange,
                MinDuration = r.MinDuration,
                MaxDuration = r.MaxDuration
            },
            YearRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.Year,
                YearRuleType = r.RuleType,
                YearValue = r.Year
            },
            BitrateRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.Bitrate,
                BitrateRuleType = r.RuleType,
                BitrateValue = r.Bitrate
            },
            StarsRuleDto r => new SmartRuleUiModel
            {
                Type = SmartRuleType.Stars,
                StarsRuleType = r.RuleType,
                StarsValue = r.Stars
            },
            _ => throw new InvalidOperationException("Unknown rule DTO type")
        };
    }
}
