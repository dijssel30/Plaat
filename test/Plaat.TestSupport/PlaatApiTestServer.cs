using System.Diagnostics;
using System.Text.Json;

namespace Plaat.TestSupport;

/// <summary>
/// Runs a real, fully-isolated instance of Plaat.Api as a child process, for tests that need genuine
/// HTTP behavior (real JWT issuance/validation, real Range-request streaming, real SignalR-free
/// request/response semantics) rather than an in-memory TestServer.
/// <para>
/// It is launched as a <b>separate OS process</b> from a private copy of a published build, on a
/// private temp SQLite database and temp media directories - deliberately not hosted in-process via
/// <c>WebApplicationFactory</c>. Plaat.Api's <c>Program.cs</c> reads its connection string and
/// <c>PlaatOptions</c> (JWT key, media paths) from a hand-rolled <c>ConfigurationBuilder</c> over
/// <c>appsettings.json</c>/<c>appsettings.Secrets.json</c> next to the running binary - not from the
/// standard ASP.NET Core configuration pipeline - so the usual "override config in
/// ConfigureWebHost" trick used for in-process test hosting would not reliably reach it. Worse, this
/// machine has a real, gitignored <c>appsettings.Secrets.json</c> pointing at the developer's actual
/// production database and media library (see CLAUDE.md's Configuration notes). Running from a
/// private copy - with that file deleted before the process ever starts - guarantees tests can never
/// read from or write to real data, regardless of how the config-loading code evolves.
/// </para>
/// </summary>
public sealed class PlaatApiTestServer : IAsyncDisposable
{
    private const string TestJwtSigningKey = "e2e-test-signing-key-do-not-use-in-production-01234567";

    public Uri BaseUri { get; private set; } = null!;
    public string AudioDirectory { get; private set; } = null!;
    public PlaatDataSeeder Seeder { get; private set; } = null!;

    private string _workingDirectory = null!;
    private Process? _process;

    /// <summary>
    /// Prepares (migrates) the database and returns the <see cref="Seeder"/> for seeding fixture
    /// data, but does not start the API process yet - call <see cref="StartAsync"/> once seeding is
    /// done, so the API's own in-process caches warm up already-populated rather than empty.
    /// </summary>
    public async Task PrepareAsync()
    {
        var publishDir = await DotnetPublishCache.GetOrPublishAsync("src/Plaat.Api/Plaat.Api.csproj", "api");

        _workingDirectory = Directory.CreateTempSubdirectory("plaat-api-test-").FullName;
        CopyDirectory(publishDir, _workingDirectory);

        // Safety net: never let this instance see the developer's real, gitignored secrets file,
        // even though we also write our own appsettings.json below that would otherwise take
        // precedence for any key it sets.
        var secretsPath = Path.Combine(_workingDirectory, "appsettings.Secrets.json");
        if (File.Exists(secretsPath))
        {
            File.Delete(secretsPath);
        }

        var databasePath = Path.Combine(_workingDirectory, "test.db");
        var imagesPath = EnsureDirectory(Path.Combine(_workingDirectory, "images"));
        var reconvertedPath = EnsureDirectory(Path.Combine(_workingDirectory, "reconverted"));
        AudioDirectory = EnsureDirectory(Path.Combine(_workingDirectory, "audio"));

        // A generous busy timeout, since the test process and this API process both hold open
        // connections to the same SQLite file (seeding happens up front, but a couple of the DB
        // integration tests also read/write it directly while the API is running).
        var connectionString = $"Data Source={databasePath};Default Timeout=10";

        Seeder = new PlaatDataSeeder(connectionString);
        Seeder.MigrateSchema();

        WriteAppSettings(connectionString, imagesPath, reconvertedPath);
    }

    public async Task StartAsync()
    {
        var port = FreePort.GetFreePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/");

        var executablePath = Path.Combine(_workingDirectory, "Plaat.Api");

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
                   ?? throw new InvalidOperationException("Failed to start the Plaat.Api test process.");

        _process.OutputDataReceived += (_, e) => { if (e.Data is not null) { Console.WriteLine($"[api stdout] {e.Data}"); } };
        _process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { Console.WriteLine($"[api stderr] {e.Data}"); } };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        await WaitUntilReadyAsync();
    }

    private async Task WaitUntilReadyAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                // stdout/stderr are already being drained line-by-line to the console (see
                // StartAsync) via BeginOutputReadLine/BeginErrorReadLine, so they can't also be read
                // synchronously here - that combination throws.
                throw new InvalidOperationException(
                    $"Plaat.Api test process exited early with code {_process.ExitCode}. See '[api stdout/stderr]' lines above for details.");
            }

            try
            {
                // Any HTTP response (even 404/405) proves Kestrel is up and past startup/migration.
                await client.GetAsync(BaseUri, HttpCompletionOption.ResponseHeadersRead);
                return;
            }
            catch (HttpRequestException)
            {
                await Task.Delay(200);
            }
        }

        throw new TimeoutException($"Plaat.Api test process at {BaseUri} did not become ready within 30s.");
    }

    private void WriteAppSettings(string connectionString, string imagesPath, string reconvertedPath)
    {
        var settings = new
        {
            Logging = new { LogLevel = new Dictionary<string, string> { ["Default"] = "Warning" } },
            AllowedHosts = "*",
            ConnectionStrings = new { SqlLiteConnection = connectionString },
            PlaatOptions = new
            {
                ImagesStoragePath = imagesPath,
                TargetReconvertedPath = reconvertedPath,
                JwtOptions = new { Key = TestJwtSigningKey },
                DiscogsSettings = new { Enabled = false },
                // Every test logs in from the same IP, far above the production login limit.
                RateLimitSettings = new { Enabled = false }
            }
        };

        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(_workingDirectory, "appsettings.json"), json);
    }

    private static string EnsureDirectory(string path)
    {
        Directory.CreateDirectory(path);
        return path;
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
