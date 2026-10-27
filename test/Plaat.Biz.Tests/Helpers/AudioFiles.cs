using System.Diagnostics;

namespace Plaat.Biz.Tests.Helpers;

/// <summary>
/// Generates tiny real audio files with ffmpeg (already a runtime dependency of Plaat.Biz) and
/// tags them with TagLib, so the tag/media-info reading paths run against genuine files.
/// </summary>
internal static class AudioFiles
{
    /// <summary>
    /// Creates a 0.2s silent file at <paramref name="path"/>; the container comes from its extension,
    /// and <paramref name="codecArguments"/> can force a codec (e.g. <c>-c:a alac</c>).
    /// </summary>
    public static async Task<string> Create(string path, Action<TagLib.File>? tag = null, params string[] codecArguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in (string[])["-loglevel", "error", "-y", "-f", "lavfi", "-i", "anullsrc=r=44100:cl=mono", "-t", "0.2", .. codecArguments, path])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed creating {path}: {error}");
        }

        if (tag is not null)
        {
            using var file = TagLib.File.Create(path);
            tag(file);
            file.Save();
        }

        return path;
    }

    /// <summary>
    /// Creates a small solid-colour JPEG image and returns its bytes.
    /// </summary>
    public static async Task<byte[]> CreateJpeg(string directory)
    {
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.jpg");
        var startInfo = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var argument in (string[])["-loglevel", "error", "-y", "-f", "lavfi", "-i", "color=c=red:s=8x8", "-frames:v", "1", path])
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var bytes = await File.ReadAllBytesAsync(path);
        File.Delete(path);

        return bytes;
    }
}
