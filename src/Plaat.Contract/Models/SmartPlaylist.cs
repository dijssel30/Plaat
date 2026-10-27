using System.Text.Json.Serialization;

namespace Plaat.Contract.Models;

/// <summary>
/// Full smart playlist details including its matching tracks.
/// </summary>
public sealed class SmartPlaylistDetailsDto : PlaylistDetailsDto;

/// <summary>
/// Minimal smart playlist summary used in listings.
/// </summary>
public sealed class SmartPlaylistOnlyDto : PlaylistOnlyDto;

/// <summary>
/// Maximum nesting depth allowed for smart playlist rule groups, shared between the API/repository
/// query builder and the frontend rule-group editor.
/// </summary>
public static class SmartPlaylistRuleGroupLimits
{
    public const int MaxGroupDepth = 3;
}

public enum SmartPlaylistGroupCombinator
{
    And,
    Or
}

/// <summary>
/// A node in the nested rule-group tree used to author a smart playlist: contains direct rules
/// and/or nested child groups, combined per <see cref="Combinator"/>.
/// </summary>
public sealed class SmartPlaylistRuleGroupDto
{
    public SmartPlaylistGroupCombinator Combinator { get; set; } = SmartPlaylistGroupCombinator.And;
    public ICollection<SmartPlaylistRuleDto> Rules { get; set; } = [];
    public ICollection<SmartPlaylistRuleGroupDto> ChildGroups { get; set; } = [];
}

public enum SmartPlaylistLimitOrderByDto
{
    RecentlyAdded
}

/// <summary>
/// Request body for creating or updating a smart playlist: its nested rule-group tree, plus an
/// optional "limit to top N" truncation applied after rule filtering. For updates, this replaces
/// the playlist's entire rule tree.
/// </summary>
public sealed class CreateSmartPlaylistRequest
{
    public required SmartPlaylistRuleGroupDto RootGroup { get; set; }
    public int? Limit { get; set; }
    public SmartPlaylistLimitOrderByDto? LimitOrderBy { get; set; }
}

/// <summary>
/// A smart playlist's current rule tree and limit settings, as returned for editing.
/// </summary>
public sealed class SmartPlaylistRulesDto
{
    public required SmartPlaylistRuleGroupDto RootGroup { get; set; }
    public int? Limit { get; set; }
    public SmartPlaylistLimitOrderByDto? LimitOrderBy { get; set; }
}

/// <summary>
/// Base type for a single smart-playlist matching rule; the concrete subtype is carried in JSON
/// via a type discriminator.
/// </summary>
[JsonDerivedType(typeof(PlayCountPlaylistRuleDto), nameof(PlayCountPlaylistRuleDto))]
[JsonDerivedType(typeof(TitlePlaylistRuleDto), nameof(TitlePlaylistRuleDto))]
[JsonDerivedType(typeof(ArtistNamePlaylistRuleDto), nameof(ArtistNamePlaylistRuleDto))]
[JsonDerivedType(typeof(AlbumNamePlaylistRuleDto), nameof(AlbumNamePlaylistRuleDto))]
[JsonDerivedType(typeof(GenrePlaylistRuleDto), nameof(GenrePlaylistRuleDto))]
[JsonDerivedType(typeof(AddedDateRuleDto), nameof(AddedDateRuleDto))]
[JsonDerivedType(typeof(AddedWithinDaysRuleDto), nameof(AddedWithinDaysRuleDto))]
[JsonDerivedType(typeof(NotPlayedInDaysRuleDto), nameof(NotPlayedInDaysRuleDto))]
[JsonDerivedType(typeof(DurationRangeRuleDto), nameof(DurationRangeRuleDto))]
[JsonDerivedType(typeof(YearRuleDto), nameof(YearRuleDto))]
[JsonDerivedType(typeof(BitrateRuleDto), nameof(BitrateRuleDto))]
[JsonDerivedType(typeof(StarsRuleDto), nameof(StarsRuleDto))]
public abstract class SmartPlaylistRuleDto;

public enum PlayCountPlaylistRuleType
{
    HigherThan,
    HigherOrEqual,
    LowerThan,
    LowerOrEqual
}

/// <summary>
/// Rule matching tracks by play count compared against a threshold.
/// </summary>
public sealed class PlayCountPlaylistRuleDto : SmartPlaylistRuleDto
{
    public PlayCountPlaylistRuleType PlayCountPlaylistRuleType { get; set; }
    public uint PlayCount { get; set; }
}

public enum DateComparisonType
{
    HigherThan,
    HigherOrEqual,
    LowerThan,
    LowerOrEqual
}

/// <summary>
/// Rule matching tracks by a date value compared against a threshold.
/// </summary>
public class DatePlaylistRuleDto : SmartPlaylistRuleDto
{
    public DateComparisonType RuleType { get; set; }
    public DateTimeOffset Value { get; set; }
}

public enum TextMatchType
{
    Contains,
    StartsWith,
    EndsWith,
    Equals
}

/// <summary>
/// Base type for rules matching tracks by a text field against a string value.
/// </summary>
public abstract class TextPlaylistRuleDto : SmartPlaylistRuleDto
{
    public TextMatchType TextMatchType { get; set; }
    public string Value { get; set; } = "";
}

/// <summary>
/// Rule matching tracks by their title.
/// </summary>
public sealed class TitlePlaylistRuleDto : TextPlaylistRuleDto;

/// <summary>
/// Rule matching tracks by artist name.
/// </summary>
public sealed class ArtistNamePlaylistRuleDto : TextPlaylistRuleDto;

/// <summary>
/// Rule matching tracks by album name.
/// </summary>
public sealed class AlbumNamePlaylistRuleDto : TextPlaylistRuleDto;

/// <summary>
/// Rule matching tracks by genre.
/// </summary>
public sealed class GenrePlaylistRuleDto : TextPlaylistRuleDto;

/// <summary>
/// Rule matching tracks by the date they were added to the library, against a fixed absolute date.
/// </summary>
public sealed class AddedDateRuleDto : DatePlaylistRuleDto;

/// <summary>
/// Rule matching tracks added within the last <see cref="Days"/> days (relative to now).
/// </summary>
public sealed class AddedWithinDaysRuleDto : SmartPlaylistRuleDto
{
    public uint Days { get; set; }
}

/// <summary>
/// Rule matching tracks that have never been played, or whose most recent play is older than
/// <see cref="Days"/> days ago.
/// </summary>
public sealed class NotPlayedInDaysRuleDto : SmartPlaylistRuleDto
{
    public uint Days { get; set; }
}

/// <summary>
/// Rule matching tracks whose duration falls within an optional min/max range. At least one bound
/// should be set.
/// </summary>
public sealed class DurationRangeRuleDto : SmartPlaylistRuleDto
{
    public TimeSpan? MinDuration { get; set; }
    public TimeSpan? MaxDuration { get; set; }
}

/// <summary>
/// Rule matching tracks by release year compared against a threshold.
/// </summary>
public sealed class YearRuleDto : SmartPlaylistRuleDto
{
    public PlayCountPlaylistRuleType RuleType { get; set; }
    public uint Year { get; set; }
}

/// <summary>
/// Rule matching tracks by bitrate compared against a threshold.
/// </summary>
public sealed class BitrateRuleDto : SmartPlaylistRuleDto
{
    public PlayCountPlaylistRuleType RuleType { get; set; }
    public int Bitrate { get; set; }
}

/// <summary>
/// Comparison operators for a star-rating rule. Unlike <see cref="PlayCountPlaylistRuleType"/> this
/// has an <see cref="Equals"/> option: ratings live on a 0-5 scale where "exactly 3 stars" is the
/// natural thing to ask for.
/// </summary>
public enum StarsComparisonType
{
    Equals,
    HigherThan,
    HigherOrEqual,
    LowerThan,
    LowerOrEqual
}

/// <summary>
/// Rule matching tracks by the star rating the smart playlist's own owner gave them. Ratings are
/// per user, so the same rule yields different tracks for different owners; a track that owner
/// never rated counts as <see cref="TrackRatingDto.MinStars"/>.
/// </summary>
public sealed class StarsRuleDto : SmartPlaylistRuleDto
{
    public StarsComparisonType RuleType { get; set; }
    public int Stars { get; set; }
}
