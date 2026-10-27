namespace Plaat.Db.Models;

/// <summary>
/// One user's star rating for one track. Ratings are per user, not per track: two users rating the
/// same track each get their own row. An absent row means "unrated", which reads as
/// <see cref="TrackRating.MinStars"/> stars everywhere - setting a track back to 0 deletes the row
/// rather than storing a zero, so the table only ever holds ratings someone actually gave.
/// </summary>
public class TrackRating
{
    public const int MinStars = 0;
    public const int MaxStars = 5;

    public int UserId { get; init; }
    public User User { get; init; } = null!;
    public uint TrackId { get; init; }
    public Track Track { get; init; } = null!;

    /// <summary>
    /// Between <see cref="MinStars"/> and <see cref="MaxStars"/> inclusive.
    /// </summary>
    public int Stars { get; set; }

    public DateTime UpdatedDate { get; set; }
}
