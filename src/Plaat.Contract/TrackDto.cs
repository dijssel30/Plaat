using Plaat.Contract.Loudness;
using Plaat.Contract.Models;

namespace Plaat.Contract;

/// <summary>
/// Minimal track summary used in listings.
/// </summary>
public class TrackDto
{
    public uint Id { get; init; }
    public required string Title { get; init; }
    public required ICollection<ArtistOnlyDto> Artists { get; init; } = [];
    public string AllArtists
    {
        get
        {
            return string.Join(", ", Artists.Select(a => a.Name));
        }
    }

    public required string Album { get; init; }
    public required uint AlbumId { get; init; }
    public required uint PlayCount { get; set; }
    public required uint TrackNumber { get; init; }
    public required uint DiscNumber { get; init; }
    public required uint Year { get; init; }
    public DateOnly? RecordedDate { get; init; }
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// Measured integrated loudness (LUFS). Null until analyzed. Defaults to unset so
    /// every existing DTO construction site keeps working unchanged.
    /// </summary>
    public float? IntegratedLoudnessLufs { get; init; }

    /// <summary>
    /// Admin-set playback trim points: null means "play from the real start"/"play to the real
    /// end". Mutable (not <c>init</c>) so the process-wide cached copies of this DTO can be patched
    /// in place when an admin edits them, the same way <see cref="PlayCount"/> is.
    /// </summary>
    public TimeSpan? TrimStart { get; set; }

    /// <summary>
    /// See <see cref="TrimStart"/>.
    /// </summary>
    public TimeSpan? TrimEnd { get; set; }

    /// <summary>
    /// Attenuate-only loudness-normalization gain (0.0-1.0, 1.0 = unchanged) to multiply into the
    /// user's own volume at playback time. Computed, not stored, so the target reference level can
    /// change later without touching stored data.
    /// </summary>
    public double AttenuationFactor
    {
        get
        {
            return LoudnessNormalization.ComputeAttenuationFactor(IntegratedLoudnessLufs);
        }
    }
}
