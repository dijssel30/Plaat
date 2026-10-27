using Microsoft.Extensions.Logging;
using NSubstitute;
using Plaat.Biz.MediaInfo;
using Shouldly;
using Xunit;

namespace Plaat.Biz.Tests;

public sealed class LoudnessAnalyzerTests
{
    [Fact]
    public async Task MeasureIntegratedLoudnessAsync_MissingFile_ReturnsNull()
    {
        var logger = Substitute.For<ILogger<LoudnessAnalyzer>>();
        var analyzer = new LoudnessAnalyzer(logger);

        var result = await analyzer.MeasureIntegratedLoudnessAsync(
            "/nonexistent/path/does-not-exist.mp3", CancellationToken.None);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task MeasureIntegratedLoudnessAsync_SynthesizedTone_ReturnsPlausibleLufs()
    {
        var path = await CreateTestToneAsync();

        try
        {
            var logger = Substitute.For<ILogger<LoudnessAnalyzer>>();
            var analyzer = new LoudnessAnalyzer(logger);

            var result = await analyzer.MeasureIntegratedLoudnessAsync(path, CancellationToken.None);

            result.ShouldNotBeNull();
            // A full-scale sine tone measures loud (well above typical music); just assert it's a
            // sane, finite LUFS value rather than pinning an exact number to ffmpeg's implementation.
            result.Value.ShouldBeInRange(-70f, 0f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ParseIntegratedLoudness_BracesInMetadataBeforeJson_StillReadsMeasurement()
    {
        // Real stderr from a track whose ID3 comment carried subtitle-style markup: the first
        // brace-delimited block in the output is not the loudnorm JSON at all.
        var stderr = """
            Input #0, mp3, from 'U (Bryan Kearney Remix).mp3':
              Metadata:
                comment         : {\an8}ripped by someone
            [Parsed_loudnorm_0 @ 0x64e17621b7c0]
            {
                    "input_i" : "-9.61",
                    "input_tp" : "0.97",
                    "input_lra" : "4.90",
                    "input_thresh" : "-19.68",
                    "output_i" : "-13.50",
                    "output_tp" : "-3.05",
                    "output_lra" : "4.50",
                    "output_thresh" : "-23.55",
                    "normalization_type" : "dynamic",
                    "target_offset" : "-0.50"
            }
            """;

        var result = CreateAnalyzer().ParseIntegratedLoudness(stderr, "/music/track.mp3");

        result.ShouldNotBeNull();
        result.Value.ShouldBe(-9.61f, 0.001f);
    }

    [Fact]
    public void ParseIntegratedLoudness_SilentInput_ReturnsNegativeInfinity()
    {
        var stderr = """{ "input_i" : "-inf", "input_tp" : "-inf" }""";

        var result = CreateAnalyzer().ParseIntegratedLoudness(stderr, "/music/silence.mp3");

        result.ShouldBe(float.NegativeInfinity);
    }

    [Fact]
    public void ParseIntegratedLoudness_NoMeasurementBlock_ReturnsNull()
    {
        var stderr = "Input #0, mp3, from 'x.mp3':\n  Metadata:\n    comment : {\\an8}nothing useful here\n";

        var result = CreateAnalyzer().ParseIntegratedLoudness(stderr, "/music/track.mp3");

        result.ShouldBeNull();
    }

    private static LoudnessAnalyzer CreateAnalyzer()
    {
        return new LoudnessAnalyzer(Substitute.For<ILogger<LoudnessAnalyzer>>());
    }

    private static async Task<string> CreateTestToneAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"plaat-loudness-test-{Guid.NewGuid():N}.wav");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("lavfi");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add("sine=frequency=1000:duration=2");
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add(path);

        using var process = new System.Diagnostics.Process { StartInfo = startInfo };
        process.Start();
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return path;
    }
}
