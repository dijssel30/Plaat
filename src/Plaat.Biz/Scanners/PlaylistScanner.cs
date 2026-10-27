using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Db;
using Plaat.Db.Repositories;

namespace Plaat.Biz.Scanners;

public partial class PlaylistScanner(
    IPlaylistRepository repository,
    IDbContextFactory<PlaatContext> contextFactory,
    FilePathReplacer replacer,
    ILogger<PlaylistScanner> logger)
{
    public async Task ImportM3uPlaylist(
        string m3uPath,
        int userId,
        CancellationToken ct)
    {
        await using var context = await contextFactory.CreateDbContextAsync(ct);

        var files = await File.ReadAllLinesAsync(m3uPath, ct);

        var tracks = new List<uint>();
        foreach (var file in files)
        {
            if (string.IsNullOrWhiteSpace(file) || file.StartsWith('#'))
            {
                continue;
            }

            var path = replacer.GetTargetPath(file);

            var trackId = await context.TrackLocations
                .Where(t => t.Path == path)
                .Select(t => t.TrackId)
                .FirstOrDefaultAsync(ct);

            if (trackId is 0)
            {
                LogPathNotInDb(file, path);

                continue;
            }

            tracks.Add(trackId);
        }

        if (tracks.Count > 0)
        {
            var playlistName = Path.GetFileNameWithoutExtension(m3uPath);

            await repository.CreatePlaylist(playlistName, userId, tracks, ct);
        }
    }

    [LoggerMessage(LogLevel.Warning, "path in m3u not found in db! {file}, path: {path}")]
    private partial void LogPathNotInDb(string file, string path);
}
