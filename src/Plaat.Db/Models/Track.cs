namespace Plaat.Db.Models;

public class Track
{
    public uint Id { get; init; }
    public uint AlbumId { get; init; }
    public Album Album { get; set; } = null!;
    public ICollection<Artist> Artists { get; set; } = [];
    public required string Title { get; set; }
    public uint TrackNumber { get; set; }
    public uint DiscNumber { get; set; }
    public TimeSpan Duration { get; set; }
    public TimeSpan? TrimStart { get; set; }
    public TimeSpan? TrimEnd { get; set; }
    public DateTime AddedDate { get; set; }
    public uint PlayCount { get; set; }
    public uint Year { get; set; }
    public DateOnly? RecordedDate { get; set; }
    public ICollection<PlaylistTrack> PlaylistTracks { get; init; } = [];
    public bool PossiblyCorrupt { get; set; }
    public int BitRate { get; set; }
    public int SampleRate { get; set; }
    public int BitsPerSample { get; set; }
    public required string FileHash { get; set; }
    public long FileSizeBytes { get; set; }
    public string? OriginalContentHash { get; set; }
    public required string MimeType { get; set; }
    public string? Format { get; set; }
    public string? Lyrics { get; set; }
    public float? IntegratedLoudnessLufs { get; set; }
    public ICollection<Genre> Genres { get; set; } = [];
    public ICollection<TrackLocation> TrackLocations { get; init; } = [];
    public ICollection<Play> Plays { get; init; } = [];
    public ICollection<TrackRating> Ratings { get; init; } = [];
}
