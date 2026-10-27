using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Plaat.Biz.MediaInfo;

/// <summary>
/// Measures a track's integrated loudness (EBU R128, in LUFS) via ffmpeg's <c>loudnorm</c> filter,
/// for playback-time attenuate-only loudness normalization. Single-pass measurement only - this
/// never applies correction to the audio, so a full two-pass loudnorm run isn't needed.
/// </summary>
public partial class LoudnessAnalyzer(ILogger<LoudnessAnalyzer> logger)
{
    private static readonly Regex LoudnormJsonRegex =
        new(@"\{[^{}]*\}", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.AllowNamedFloatingPointLiterals
    };

    /// <summary>
    /// Returns the measured integrated loudness in LUFS, or null if it could not be measured
    /// (missing ffmpeg, corrupt/unreadable file, unparseable output) - non-fatal, matches the
    /// existing PossiblyCorrupt handling elsewhere in the scanner.
    /// </summary>
    public async Task<float?> MeasureIntegratedLoudnessAsync(string path, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -nostdin: this runs headless, so ffmpeg has no business reading the host's stdin. Without
        // it ffmpeg grabs whatever stdin it inherited looking for interactive keys, and a stray byte
        // (or a closed handle) makes it quit mid-measurement.
        startInfo.ArgumentList.Add("-nostdin");
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(path);
        startInfo.ArgumentList.Add("-af");
        startInfo.ArgumentList.Add("loudnorm=I=-14:TP=-1.5:LRA=11:print_format=json");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("null");
        startInfo.ArgumentList.Add("-");

        using var process = new Process();
        process.StartInfo = startInfo;

        try
        {
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync(ct);
            var standardErrorTask = process.StandardError.ReadToEndAsync(ct);

            await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync(ct));

            var error = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                // ffmpeg's exit code is its own AVERROR truncated to 8 bits (254 = file not found,
                // 183 = invalid data, 234 = told to quit via stdin), with one exception: 255 means it
                // was signalled rather than that the file is bad - SIGTERM/SIGINT reaching the whole
                // process group, which is what a host shutdown or a `systemctl restart` looks like
                // from here. The code alone doesn't say which, so log ffmpeg's own last words too.
                if (ct.IsCancellationRequested)
                {
                    // We're shutting down and took the process group's SIGTERM with us; the track
                    // keeps its null loudness and gets picked up again on the next run.
                    return null;
                }

                LogAnalysisFailed(process.ExitCode, path, Tail(error));
                return null;
            }

            return ParseIntegratedLoudness(error, path);
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a problem with the file.
            return null;
        }
        catch (Exception ex)
        {
            LogMeasureFailed(ex, path);
            return null;
        }
    }

    internal float? ParseIntegratedLoudness(string ffmpegStderr, string path)
    {
        // loudnorm prints its JSON at the very end, but everything ffmpeg logged before it is fair
        // game for braces too - tag values copied from subtitle/karaoke text ("{\an8}...") show up
        // in the Metadata block and used to be matched first, then handed to the deserializer. Walk
        // the matches back to front and only accept a block that carries the measurement itself.
        foreach (var match in LoudnormJsonRegex.Matches(ffmpegStderr).Reverse())
        {
            if (!match.Value.Contains("\"input_i\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                var measurement = JsonSerializer.Deserialize<LoudnormMeasurement>(match.Value, JsonSerializerOptions);

                return ParseLufs(measurement?.InputIntegratedLoudness);
            }
            catch (JsonException ex)
            {
                LogParseFailed(ex, path);
                return null;
            }
        }

        LogNoMeasurementJson(path);
        return null;
    }

    /// <summary>
    /// The last few lines of ffmpeg's stderr - where it says what actually went wrong. The rest is
    /// the banner and the per-stream dump, which is noise in a log line.
    /// </summary>
    private static string Tail(string ffmpegStderr)
    {
        var lines = ffmpegStderr
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .TakeLast(5);

        return string.Join(" | ", lines);
    }

    private static float? ParseLufs(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        // Fully silent input measures as "-inf" - not a boostable/attenuable value; treat as
        // very quiet so the attenuate-only formula naturally leaves it unchanged (factor clamps to 1.0).
        return value.Trim().Equals("-inf", StringComparison.OrdinalIgnoreCase)
            ? float.NegativeInfinity
            : null;
    }

    private sealed class LoudnormMeasurement
    {
        [JsonPropertyName("input_i")]
        public string? InputIntegratedLoudness { get; set; }
    }

    [LoggerMessage(LogLevel.Warning, "ffmpeg loudness analysis exited with code {ExitCode} for file {Path}: {FfmpegError}")]
    private partial void LogAnalysisFailed(int exitCode, string path, string ffmpegError);

    [LoggerMessage(LogLevel.Warning, "Unable to measure loudness for file {Path}")]
    private partial void LogMeasureFailed(Exception ex, string path);

    [LoggerMessage(LogLevel.Warning, "ffmpeg loudnorm produced no measurement JSON for file {Path}")]
    private partial void LogNoMeasurementJson(string path);

    [LoggerMessage(LogLevel.Warning, "Unable to parse ffmpeg loudnorm output for file {Path}")]
    private partial void LogParseFailed(Exception ex, string path);
}
