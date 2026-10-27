using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace Plaat.Biz.MediaInfo.Models;

[PublicAPI]
public sealed class MediaInfoResult
{
    [JsonPropertyName("media")]
    public Media? Media { get; init; }
}

public sealed class Media
{
    [JsonPropertyName("track")]
    public IReadOnlyCollection<Track> Tracks { get; init; } = [];
}

public class Track
{
    [JsonPropertyName("@type")]
    public string? Type { get; set; }

    [JsonPropertyName("AudioCount")]
    public string? AudioCount { get; set; }

    [JsonPropertyName("FileExtension")]
    public string? FileExtension { get; set; }

    [JsonPropertyName("Format")]
    public string? Format { get; set; }

    [JsonPropertyName("Format_Profile")]
    public string? FormatProfile { get; set; }

    [JsonPropertyName("CodecID")]
    public string? CodecId { get; set; }

    [JsonPropertyName("CodecID_Compatible")]
    public string? CodecIdCompatible { get; set; }

    [JsonPropertyName("FileSize")]
    public string? FileSize { get; set; }

    [JsonPropertyName("Duration")]
    public string? Duration { get; set; }

    [JsonPropertyName("OverallBitRate_Mode")]
    public string? OverallBitRateMode { get; set; }

    [JsonPropertyName("OverallBitRate")]
    public string? OverallBitRate { get; set; }

    [JsonPropertyName("StreamSize")]
    public string? StreamSize { get; set; }

    [JsonPropertyName("HeaderSize")]
    public string? HeaderSize { get; set; }

    [JsonPropertyName("DataSize")]
    public string? DataSize { get; set; }

    [JsonPropertyName("FooterSize")]
    public string? FooterSize { get; set; }

    [JsonPropertyName("IsStreamable")]
    public string? IsStreamable { get; set; }

    [JsonPropertyName("Title")]
    public string? Title { get; set; }

    [JsonPropertyName("Album")]
    public string? Album { get; set; }

    [JsonPropertyName("Album_Performer")]
    public string? AlbumPerformer { get; set; }

    [JsonPropertyName("Part_Position")]
    public string? PartPosition { get; set; }

    [JsonPropertyName("Part_Position_Total")]
    public string? PartPositionTotal { get; set; }

    [JsonPropertyName("Track")]
    public string? TrackTitle { get; set; }

    [JsonPropertyName("Track_Position")]
    public string? TrackPosition { get; set; }

    [JsonPropertyName("Track_Position_Total")]
    public string? TrackPositionTotal { get; set; }

    [JsonPropertyName("Performer")]
    public string? Performer { get; set; }

    [JsonPropertyName("Genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("Recorded_Date")]
    public string? RecordedDate { get; set; }

    [JsonPropertyName("Encoded_Date")]
    public string? EncodedDate { get; set; }

    [JsonPropertyName("Tagged_Date")]
    public string? TaggedDate { get; set; }

    [JsonPropertyName("File_Modified_Date")]
    public string? FileModifiedDate { get; set; }

    [JsonPropertyName("File_Modified_Date_Local")]
    public string? FileModifiedDateLocal { get; set; }

    [JsonPropertyName("Encoded_Application")]
    public string? EncodedApplication { get; set; }

    [JsonPropertyName("Cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("Lyrics")]
    public string? Lyrics { get; set; }

    [JsonPropertyName("StreamOrder")]
    public string? StreamOrder { get; set; }

    [JsonPropertyName("ID")]
    public string? Id { get; set; }

    [JsonPropertyName("Duration_LastFrame")]
    public string? DurationLastFrame { get; set; }

    [JsonPropertyName("BitRate_Mode")]
    public string? BitRateMode { get; set; }

    [JsonPropertyName("BitRate")]
    public string? BitRate { get; set; }

    [JsonPropertyName("Channels")]
    public string? Channels { get; set; }

    [JsonPropertyName("SamplingRate")]
    public string? SamplingRate { get; set; }

    [JsonPropertyName("SamplingCount")]
    public string? SamplingCount { get; set; }

    [JsonPropertyName("FrameCount")]
    public string? FrameCount { get; set; }

    [JsonPropertyName("BitDepth")]
    public string? BitDepth { get; set; }

    [JsonPropertyName("extra")]
    public IReadOnlyDictionary<string, string?>? Extra { get; set; }
}
