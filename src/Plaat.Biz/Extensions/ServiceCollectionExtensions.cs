using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plaat.Biz.BackgroundServices;
using Plaat.Biz.Logging;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Qaac;
using Plaat.Biz.Scanners;
using Plaat.Biz.Workers;
using Plaat.Infrastructure.Discogs;

namespace Plaat.Biz.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddBizServices(this IServiceCollection services)
    {
        services.AddTransient<IMusicScanner, MusicScanner>();
        services.AddTransient<IItunesLibraryImporter, ItunesLibraryImporter>();
        services.AddTransient<LibrarySearchService>();
        services.AddSingleton<MediaInfoParser>();
        services.AddSingleton<LoudnessAnalyzer>();
        services.AddSingleton<LoudnessAnalysisQueueService>();
        services.AddSingleton<ConvertQueueService>();
        services.AddSingleton<QaacHelper>();
        services.AddSingleton<PlaylistScanner>();
        services.AddSingleton<FilePathReplacer>();
        services.AddSingleton<MonitoredFolderScanService>();
        services.AddSingleton<SmartPlaylistCountService>();
        services.AddSingleton<DatabaseBackupService>();

        services.AddHttpClient();
        services.AddDiscogsClient();
        services.AddSingleton<ArtistEnrichmentQueueService>();

        services.AddHostedService<ConvertQueueBackgroundService>();
        services.AddHostedService<LoudnessAnalysisBackgroundService>();
        services.AddHostedService<MonitoredFolderScanBackgroundService>();
        services.AddHostedService<SmartPlaylistCountRecalculationBackgroundService>();
        services.AddHostedService<DatabaseBackupBackgroundService>();

        services.AddSingleton<DatabaseLoggerProvider>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<DatabaseLoggerProvider>());
    }
}
