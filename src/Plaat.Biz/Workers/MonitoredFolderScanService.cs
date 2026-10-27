using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Scanners;
using Plaat.Db;

namespace Plaat.Biz.Workers;

public partial class MonitoredFolderScanService : IDisposable
{
    private readonly IDbContextFactory<PlaatContext> _contextFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MonitoredFolderScanService> _logger;
    private readonly Channel<ScanRequest> _scanRequests = Channel.CreateBounded<ScanRequest>(1);
    private int _scanBusy;
    private volatile string? _currentFolder;

    private enum ScanRequestKind
    {
        AllMonitoredFolders,
        Folder,
        Csv,
        ItunesXml
    }

    private readonly record struct ItunesImportOptions(int UserId, bool ImportMissingTracks);

    private readonly record struct ScanRequest(ScanRequestKind Kind, string? Path, ItunesImportOptions? ItunesOptions = null);

    // ReSharper disable once NotAccessedField.Local
    // Task.Run runs in background
    private readonly Task _processingLoop;

    public MonitoredFolderScanService(
        IDbContextFactory<PlaatContext> contextFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<MonitoredFolderScanService> logger)
    {
        _contextFactory = contextFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _processingLoop = Task.Run(() => ProcessScanRequestsAsync(CancellationToken.None));
    }

    /// <summary>
    /// Whether a scan is currently running on the background processing loop.
    /// </summary>
    public bool IsBusy
    {
        get
        {
            return Volatile.Read(ref _scanBusy) != 0;
        }
    }

    /// <summary>
    /// The folder currently being scanned, or null if idle.
    /// </summary>
    public string? CurrentFolder
    {
        get
        {
            return _currentFolder;
        }
    }

    /// <summary>
    /// Requests a scan of all monitored folders on the background processing loop if none is
    /// currently running. Returns whether a new scan was started.
    /// </summary>
    public bool TryStartScan()
    {
        return TryEnqueue(new ScanRequest(ScanRequestKind.AllMonitoredFolders, null));
    }

    /// <summary>
    /// Requests a scan of a single, ad-hoc folder (one not registered as a monitored folder) on
    /// the background processing loop if none is currently running. Returns whether a new scan
    /// was started.
    /// </summary>
    public bool TryStartFolderScan(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        return TryEnqueue(new ScanRequest(ScanRequestKind.Folder, folderPath));
    }

    /// <summary>
    /// Requests an import of the tracks listed in a CSV file on the background processing loop if
    /// none is currently running. Returns whether a new scan was started.
    /// </summary>
    public bool TryStartCsvScan(string csvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);

        return TryEnqueue(new ScanRequest(ScanRequestKind.Csv, csvPath));
    }

    /// <summary>
    /// Requests an import of ratings, play counts and trim points from an iTunes/Music Library.xml
    /// file on the background processing loop if none is currently running. Returns whether a new
    /// import was started.
    /// </summary>
    public bool TryStartItunesXmlImport(string xmlPath, int userId, bool importMissingTracks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlPath);

        return TryEnqueue(new ScanRequest(
            ScanRequestKind.ItunesXml, xmlPath, new ItunesImportOptions(userId, importMissingTracks)));
    }

    private bool TryEnqueue(ScanRequest request)
    {
        if (Interlocked.CompareExchange(ref _scanBusy, 1, 0) != 0)
        {
            return false;
        }

        if (!_scanRequests.Writer.TryWrite(request))
        {
            Volatile.Write(ref _scanBusy, 0);
            return false;
        }

        return true;
    }

    private async Task ProcessScanRequestsAsync(CancellationToken ct)
    {
        await foreach (var request in _scanRequests.Reader.ReadAllAsync(ct))
        {
            try
            {
                switch (request.Kind)
                {
                    case ScanRequestKind.AllMonitoredFolders:
                        await ScanAllFolders(ct);
                        break;
                    case ScanRequestKind.Folder:
                        await ScanFolder(request.Path!, ct);
                        break;
                    case ScanRequestKind.Csv:
                        await ScanCsv(request.Path!, ct);
                        break;
                    case ScanRequestKind.ItunesXml:
                        await ImportItunesXml(request.Path!, request.ItunesOptions!.Value, ct);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogScanFailed(ex);
            }
            finally
            {
                _currentFolder = null;
                Interlocked.Exchange(ref _scanBusy, 0);
            }
        }
    }

    private async Task ScanAllFolders(CancellationToken ct)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(ct);

        var folders = await context.MonitoredFolders.ToListAsync(ct);

        using var scope = _scopeFactory.CreateScope(); // create a new scope to ensure track factory is new
        var scanner = scope.ServiceProvider.GetRequiredService<IMusicScanner>();

        foreach (var folder in folders)
        {
            _currentFolder = folder.Path;

            try
            {
                await scanner.LoadFilesFromFolder(folder.Path, ct);
                folder.LastScannedAt = DateTimeOffset.UtcNow;
            }
            catch (Exception ex)
            {
                LogFolderScanFailed(ex, folder.Path);
            }
        }

        await context.SaveChangesAsync(ct);
    }

    private async Task ScanFolder(string folderPath, CancellationToken ct)
    {
        _currentFolder = folderPath;

        using var scope = _scopeFactory.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<IMusicScanner>();

        await scanner.LoadFilesFromFolder(folderPath, ct);
    }

    private async Task ScanCsv(string csvPath, CancellationToken ct)
    {
        _currentFolder = csvPath;

        using var scope = _scopeFactory.CreateScope();
        var scanner = scope.ServiceProvider.GetRequiredService<IMusicScanner>();

        await scanner.LoadFilesFromCsv(csvPath, ct);
    }

    private async Task ImportItunesXml(string xmlPath, ItunesImportOptions importOptions, CancellationToken ct)
    {
        _currentFolder = xmlPath;

        using var scope = _scopeFactory.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<IItunesLibraryImporter>();

        await importer.Import(xmlPath, importOptions.UserId, importOptions.ImportMissingTracks, ct);
    }

    public void Dispose()
    {
        _scanRequests.Writer.TryComplete();
    }

    [LoggerMessage(LogLevel.Error, "Monitored folder scan failed")]
    private partial void LogScanFailed(Exception ex);

    [LoggerMessage(LogLevel.Error, "Failed to scan monitored folder {Path}")]
    private partial void LogFolderScanFailed(Exception ex, string path);
}
