using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plaat.Biz.MediaInfo.Models;

namespace Plaat.Biz.MediaInfo;

public partial class MediaInfoParser(ILogger<MediaInfoParser> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new MediaInfoStringConverter() }
    };

    public async Task<MediaInfoResult?> GetMediaInfoResult(string file)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "mediainfo",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("--output=JSON");
        startInfo.ArgumentList.Add(file);

        using var process = new Process();
        process.StartInfo = startInfo;

        try
        {
            process.Start();

            var standardOutputTask = process.StandardOutput.ReadToEndAsync();
            var standardErrorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync());

            var output = await standardOutputTask;
            var error = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                LogMediaInfoFailed(process.ExitCode, file, error);
                return null;
            }

            return JsonSerializer.Deserialize<MediaInfoResult>(output, JsonOptions);
        }
        catch (Exception ex)
        {
            LogParseFailed(ex, file);
            return null;
        }
    }

    [LoggerMessage(LogLevel.Error, "mediainfo exited with code {ExitCode} for file {file}: {error}")]
    private partial void LogMediaInfoFailed(int exitCode, string file, string error);

    [LoggerMessage(LogLevel.Error, "Unable to parse mediainfo for file {file}")]
    private partial void LogParseFailed(Exception ex, string file);
}
