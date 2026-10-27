using System.Collections.Concurrent;
using System.Diagnostics;

namespace Plaat.TestSupport;

/// <summary>
/// Publishes a project exactly once per test process (self-contained/single-file publishes - see
/// Plaat.Api.csproj/Plaat.Frontend.csproj - are the slow part) and hands every caller a path to that
/// shared, read-only output. Callers then copy it into their own working directory rather than
/// running from the shared one directly, since every instance needs its own appsettings.json/etc.
/// <para>
/// Publishes to the <b>same</b> fixed output directory every time (never a fresh temp dir) and lets
/// `dotnet publish`'s own MSBuild up-to-date checks decide what actually needs rebuilding. A cold
/// first publish still takes a couple of minutes (self-contained output is large), but once that
/// directory exists, an unrelated code change elsewhere - or a full rerun with no changes at all -
/// finishes in seconds instead of repeating the whole self-contained copy every time.
/// </para>
/// </summary>
internal static class DotnetPublishCache
{
    private static readonly ConcurrentDictionary<string, Lazy<Task<string>>> Cache = new();

    /// <param name="projectRelativePath">Project path relative to the repo root, e.g. "Plaat.Api/Plaat.Api.csproj".</param>
    /// <param name="cacheKey">A short, filesystem-safe, unique name for this project's cached output directory.</param>
    public static Task<string> GetOrPublishAsync(string projectRelativePath, string cacheKey)
    {
        return Cache.GetOrAdd(cacheKey, _ => new Lazy<Task<string>>(() => PublishAsync(projectRelativePath, cacheKey))).Value;
    }

    private static async Task<string> PublishAsync(string projectRelativePath, string cacheKey)
    {
        var repoRoot = FindRepoRoot();
        var csprojPath = Path.Combine(repoRoot, projectRelativePath.Replace('/', Path.DirectorySeparatorChar));
        var outputDir = Path.Combine(Path.GetTempPath(), $"plaat-e2e-{cacheKey}-publish");

        // `dotnet publish` into an *existing* output directory has been observed to silently skip
        // recopying native assets (e.g. libe_sqlite3.so) that its up-to-date check wrongly considers
        // already present, leaving a broken publish that throws DllNotFoundException at runtime.
        // Clearing the output directory first forces a full, correct copy on every publish; the
        // *compile* speed win this cache exists for comes from each project's own bin/obj (untouched
        // here), not from reusing files already sitting in outputDir, so this doesn't cost much.
        if (Directory.Exists(outputDir))
        {
            Directory.Delete(outputDir, recursive: true);
        }

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("publish");
        startInfo.ArgumentList.Add(csprojPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputDir);

        // Reusable MSBuild worker nodes outlive the publish and inherit its redirected stdout/stderr,
        // so reading those to end-of-stream below would wait for the nodes to exit (minutes, or
        // never) instead of for the publish itself.
        startInfo.ArgumentList.Add("-nodeReuse:false");
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start 'dotnet publish' for {projectRelativePath}.");

        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet publish' for {projectRelativePath} failed with exit code {process.ExitCode}.\n--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }

        return outputDir;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Plaat.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root (Plaat.slnx) from " + AppContext.BaseDirectory);
    }
}
