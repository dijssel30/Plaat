namespace Plaat.Db.Models;

public class SmartPlaylist
{
    public uint Id { get; init; }
    public required string Name { get; init; }
    public int UserId { get; init; }
    public User User { get; init; } = null!;
    public bool IsPinned { get; init; }

    public uint RootGroupId { get; set; }
    public SmartPlaylistRuleGroup RootGroup { get; init; } = null!;

    public int TrackCount { get; set; }
    public DateTime TrackCountUpdatedAt { get; set; }

    public int? Limit { get; set; }
    public SmartPlaylistLimitOrderBy? LimitOrderBy { get; set; }
}

public enum SmartPlaylistLimitOrderBy
{
    RecentlyAdded
}

public enum SmartPlaylistGroupCombinator
{
    And,
    Or
}

public class SmartPlaylistRuleGroup
{
    public uint Id { get; init; }
    public SmartPlaylistGroupCombinator Combinator { get; init; }

    public uint? ParentGroupId { get; init; }
    public SmartPlaylistRuleGroup? ParentGroup { get; init; }
    public ICollection<SmartPlaylistRuleGroup> ChildGroups { get; init; } = [];

    public ICollection<SmartPlaylistRule> Rules { get; init; } = [];
}

public class SmartPlaylistRule
{
    public uint Id { get; init; }
    public uint GroupId { get; init; }
    public SmartPlaylistRuleGroup Group { get; init; } = null!;
}

public enum PlayCountPlaylistRuleType
{
    HigherThan,
    HigherOrEqual,
    LowerThan,
    LowerOrEqual
}

/// <summary>
/// Comparison operators for a star-rating rule. Unlike <see cref="PlayCountPlaylistRuleType"/>
/// this has an <see cref="Equals"/> option: ratings live on a 0-5 scale where "exactly 3 stars" is
/// the natural thing to ask for, and expressing it as two separate >=/&lt;= rules would be clumsy.
/// </summary>
public enum StarsPlaylistRuleType
{
    Equals,
    HigherThan,
    HigherOrEqual,
    LowerThan,
    LowerOrEqual
}

public enum TextMatchType
{
    Contains,
    StartsWith,
    EndsWith,
    Equals
}

public sealed class PlayCountPlaylistRule : SmartPlaylistRule
{
    public PlayCountPlaylistRuleType PlayCountPlaylistRuleType { get; init; }
    public uint PlayCount { get; init; }
}

public class DateTimePlaylistRule : SmartPlaylistRule
{
    public PlayCountPlaylistRuleType DateRuleMatchType { get; init; }
    public required DateTimeOffset Value { get; init; }
}

public abstract class TextPlaylistRule : SmartPlaylistRule
{
    public TextMatchType TextMatchType { get; init; }
    public required string Value { get; init; }
}

public sealed class TitlePlaylistRule : TextPlaylistRule;

public sealed class ArtistNamePlaylistRule : TextPlaylistRule;

public sealed class AlbumNamePlaylistRule : TextPlaylistRule;

public sealed class GenrePlaylistRule : TextPlaylistRule;

public sealed class AddedDateRule : DateTimePlaylistRule;

/// <summary>
/// Matches tracks added within the last <see cref="Days"/> days (relative to now), as opposed to
/// <see cref="AddedDateRule"/> which compares against a fixed absolute date.
/// </summary>
public sealed class AddedWithinDaysPlaylistRule : SmartPlaylistRule
{
    public uint Days { get; init; }
}

/// <summary>
/// Matches tracks that have no <see cref="Play"/> at all, or whose most recent play is older than
/// <see cref="Days"/> days ago. A never-played track counts as matching.
/// </summary>
public sealed class NotPlayedInDaysPlaylistRule : SmartPlaylistRule
{
    public uint Days { get; init; }
}

public sealed class DurationRangePlaylistRule : SmartPlaylistRule
{
    public TimeSpan? MinDuration { get; init; }
    public TimeSpan? MaxDuration { get; init; }
}

public sealed class YearPlaylistRule : SmartPlaylistRule
{
    public PlayCountPlaylistRuleType YearRuleMatchType { get; init; }
    public uint Year { get; init; }
}

public sealed class BitratePlaylistRule : SmartPlaylistRule
{
    public PlayCountPlaylistRuleType BitrateRuleMatchType { get; init; }
    public int Bitrate { get; init; }
}

/// <summary>
/// Matches tracks by the star rating the smart playlist's own owner gave them (see
/// <see cref="TrackRating"/>). Tracks that user never rated count as
/// <see cref="TrackRating.MinStars"/>.
/// </summary>
public sealed class StarsPlaylistRule : SmartPlaylistRule
{
    public StarsPlaylistRuleType StarsRuleMatchType { get; init; }
    public int Stars { get; init; }
}
