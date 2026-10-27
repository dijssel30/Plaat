using System.Diagnostics;
using System.Text.Json;

namespace Plaat.TestSupport;

/// <summary>
/// Runs a real instance of Plaat.Frontend as a real child process, on a real Kestrel socket a
/// browser can actually connect to.
/// <para>
/// This started out hosted in-process via <c>WebApplicationFactory</c> with <c>CreateHost</c>
/// overridden to build a real Kestrel host instead of the usual in-memory <c>TestServer</c> - the
/// commonly documented trick for browser-driven Blazor Server tests. In this hosting setup, though,
/// <c>WebApplicationFactory</c>'s own internal wiring still registers <c>TestServer</c> as the
/// resolved <c>IServer</c> ahead of anything set via <c>UseUrls</c>/<c>ConfigureWebHost</c>, so the
/// app never actually listened on a real socket - Playwright's real browser got a bare
/// ERR_CONNECTION_REFUSED no matter what port was reported back. Running it as a genuine separate
/// process - the same proven approach <see cref="PlaatApiTestServer"/> uses - sidesteps that
/// entirely. Unlike Plaat.Api, Plaat.Frontend's own appsettings.json has no sensitive/absolute paths
/// (just a default backend URL, see CLAUDE.md), so there's no need to strip a secrets file here.
/// </para>
/// </summary>
public sealed class PlaatFrontendTestServer : IAsyncDisposable
{
    public Uri BaseUri { get; private set; } = null!;

    public required Uri BackendBaseUri { get; init; }

    private string _workingDirectory = null!;
    private Process? _process;

    public async Task StartAsync()
    {
        var publishDir = await DotnetPublishCache.GetOrPublishAsync("src/Plaat.Frontend/Plaat.Frontend.csproj", "frontend");

        _workingDirectory = Directory.CreateTempSubdirectory("plaat-frontend-test-").FullName;
        CopyDirectory(publishDir, _workingDirectory);

        var port = FreePort.GetFreePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/");

        WriteAppSettings();

        var executablePath = Path.Combine(_workingDirectory, "Plaat.Frontend");
        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                ["ASPNETCORE_URLS"] = BaseUri.ToString(),
                ["ASPNETCORE_ENVIRONMENT"] = "Development"
            }
        };

        _process = Process.Start(startInfo)
                   ?? throw new InvalidOperationException("Failed to start the Plaat.Frontend test process.");

        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) { Console.WriteLine($"[frontend stdout] {e.Data}"); } };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { Console.WriteLine($"[frontend stderr] {e.Data}"); } };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilReadyAsync();
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient();
        client.Timeout = TimeSpan.FromSeconds(5);
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                // stdout/stderr are already being drained line-by-line to the console (see
                // StartAsync) via BeginOutputReadLine/BeginErrorReadLine, so they can't also be read
                // synchronously here - that combination throws.
                throw new InvalidOperationException(
                    $"Plaat.Frontend test process exited early with code {_process.ExitCode}. See '[frontend stdout/stderr]' lines above for details.");
            }

            try
            {
                await client.GetAsync(BaseUri, HttpCompletionOption.ResponseHeadersRead);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException($"Plaat.Frontend test process at {BaseUri} did not become ready within 30s.");
    }

    private void WriteAppSettings()
    {
        var settings = new
        {
            Logging = new { LogLevel = new Dictionary<string, string> { ["Default"] = "Warning" } },
            AllowedHosts = "*",
            // Blazor Server hides circuit exception details by default regardless of environment
            // (Plaat.Frontend/Program.cs never overrides CircuitOptions.DetailedErrors) - surfacing
            // them here makes a failing test's captured browser console output actually diagnosable.
            DetailedErrors = true,
            PlaatBackendOptions = new { BaseUri = BackendBaseUri.ToString() }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_workingDirectory, "appsettings.json"), json);
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }

        foreach (var directory in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch
            {
                // best-effort cleanup
            }
        }

        _process?.Dispose();

        try
        {
            if (_workingDirectory is not null && Directory.Exists(_workingDirectory))
            {
                Directory.Delete(_workingDirectory, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup; leftover temp dirs under the OS temp folder are harmless
        }
    }
}
