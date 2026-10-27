using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Options;

namespace Plaat.Biz.Qaac;

public sealed partial class QaacHelper(
    ILogger<QaacHelper> logger,
    PlaatOptions plaatOptions)
{
    private const string WineBinPath = "/usr/bin/wine";

    public async Task ConvertFileUsingQaac(
        string file,
        string outputFile)
    {
        if (plaatOptions.QaacPath is null
            || !File.Exists(plaatOptions.QaacPath))
        {
            throw new InvalidOperationException("Qaac path not found");
        }

        if (OperatingSystem.IsLinux()
            && !File.Exists(WineBinPath))
        {
            throw new InvalidOperationException("Application is running in Linux environment, but Wine is not installed in expected folder" +
                                                "Wine is needed to run Qaac on Linux. If you don't want to use Qaac, set UseQaac to false.");
        }
        
        var fileName = OperatingSystem.IsWindows()
            ? plaatOptions.QaacPath
            : "/usr/bin/wine";

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (OperatingSystem.IsLinux())
        {
            startInfo.ArgumentList.Add(plaatOptions.QaacPath);
        }

        startInfo.ArgumentList.Add("--tvbr");
        startInfo.ArgumentList.Add("91");
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(outputFile);
        startInfo.ArgumentList.Add(file);

        using var process = new Process();
        process.StartInfo = startInfo;

        process.Start();

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();

        await Task.WhenAll(standardOutputTask, standardErrorTask, process.WaitForExitAsync());

        var output = await standardOutputTask;
        var error = await standardErrorTask;

        if (!string.IsNullOrWhiteSpace(output))
        {
            LogOutput(output);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            LogError(error);
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"qaac exited with code {process.ExitCode} while converting {file}: {error}");
        }
    }

    [LoggerMessage(LogLevel.Debug, "{Output}")]
    private partial void LogOutput(string output);

    [LoggerMessage(LogLevel.Warning, "{Error}")]
    private partial void LogError(string error);
}
