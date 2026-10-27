using System.Text.Json;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.MediaInfo.Models;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class MediaInfoResultTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new MediaInfoStringConverter() }
    };

    [Fact]
    public void Deserialize_PlainStringFields_ReadsThemNormally()
    {
        const string json = """
            {
              "media": {
                "track": [
                  { "@type": "General", "Title": "Some Title" }
                ]
              }
            }
            """;

        var result = JsonSerializer.Deserialize<MediaInfoResult>(json, Options);

        result!.Media!.Tracks.Single().Title.ShouldBe("Some Title");
    }

    [Fact]
    public void Deserialize_ObjectValuedExtraField_DoesNotThrowAndExtractsValue()
    {
        // Reproduces mediainfo emitting a binary/base64-wrapped value instead of a plain string
        // for some ID3/MP4 frames (e.g. extra.Publisher_Url, extra.linf).
        const string json = """
            {
              "media": {
                "track": [
                  {
                    "@type": "Audio",
                    "extra": {
                      "MD5_Unencoded": "abc123",
                      "Publisher_Url": { "@dt": "binary.base64", "#value": "aHR0cDovL2V4YW1wbGUuY29t" }
                    }
                  }
                ]
              }
            }
            """;

        var result = JsonSerializer.Deserialize<MediaInfoResult>(json, Options);

        var track = result!.Media!.Tracks.Single();
        track.Extra!["MD5_Unencoded"].ShouldBe("abc123");
        track.Extra["Publisher_Url"].ShouldBe("aHR0cDovL2V4YW1wbGUuY29t");
    }

    [Fact]
    public void Deserialize_ObjectValuedLyricsField_DoesNotThrow()
    {
        const string json = """
            {
              "media": {
                "track": [
                  {
                    "@type": "General",
                    "Lyrics": { "@dt": "binary.base64", "#value": "c29tZSBseXJpY3M=" }
                  }
                ]
              }
            }
            """;

        var result = JsonSerializer.Deserialize<MediaInfoResult>(json, Options);

        result!.Media!.Tracks.Single().Lyrics.ShouldBe("c29tZSBseXJpY3M=");
    }

    [Fact]
    public void Deserialize_ObjectValueWithoutValueProperty_KeepsRawJson()
    {
        const string json = """{ "media": { "track": [ { "@type": "General", "Title": { "@dt": "binary" } } ] } }""";

        var result = JsonSerializer.Deserialize<MediaInfoResult>(json, Options);

        result!.Media!.Tracks.Single().Title.ShouldBe("""{ "@dt": "binary" }""");
    }

    [Fact]
    public void Deserialize_NullField_IsNull()
    {
        const string json = """{ "media": { "track": [ { "@type": "General", "Title": null } ] } }""";

        var result = JsonSerializer.Deserialize<MediaInfoResult>(json, Options);

        result!.Media!.Tracks.Single().Title.ShouldBeNull();
    }

    [Fact]
    public void Serialize_WritesPlainString()
    {
        var json = JsonSerializer.Serialize(new Track { Type = "Audio" }, Options);

        json.ShouldContain("\"Audio\"");
    }
}
