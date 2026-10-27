using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plaat.Biz.MediaInfo;

/// <summary>
/// Mediainfo's JSON output normally represents field values as plain strings, but for some binary
/// ID3/MP4 frames (e.g. URL frames, embedded lyrics) it instead emits an object like
/// <c>{"@dt": "binary.base64", "#value": "..."}</c>. Tolerate that shape wherever a string is expected
/// instead of throwing, since we only ever consume a handful of well-known plain-string fields.
/// </summary>
public sealed class MediaInfoStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.StartObject:
            {
                using var document = JsonDocument.ParseValue(ref reader);

                return document.RootElement.TryGetProperty("#value", out var value)
                    ? value.GetString()
                    : document.RootElement.GetRawText();
            }
            default:
                return reader.GetString();
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
