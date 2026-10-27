using System.Data;
using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for the set of filesystem folders monitored for library scans.
/// </summary>
public interface IMonitoredFolderRepository
{
    /// <summary>
    /// Gets all monitored folders, ordered by path.
    /// </summary>
    Task<IReadOnlyCollection<MonitoredFolderDto>> GetAll(CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new folder to monitor. Throws if the path is already monitored.
    /// </summary>
    Task<MonitoredFolderDto> AddFolder(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Stops monitoring a folder.
    /// </summary>
    Task RemoveFolder(uint id, CancellationToken cancellationToken);
}

internal sealed class MonitoredFolderRepository(IDbContextFactory<PlaatContext> dbContextFactory)
    : IMonitoredFolderRepository
{
    public async Task<IReadOnlyCollection<MonitoredFolderDto>> GetAll(CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        return await context.MonitoredFolders
            .OrderBy(f => f.Path)
            .Select(f => new MonitoredFolderDto(f.Id, f.Path, f.LastScannedAt))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<MonitoredFolderDto> AddFolder(string path, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (await context.MonitoredFolders.AnyAsync(f => f.Path == path, cancellationToken))
        {
            throw new DuplicateNameException("Folder is already monitored");
        }

        var folder = new MonitoredFolder { Path = path };

        context.Add(folder);

        await context.SaveChangesAsync(cancellationToken);

        return new MonitoredFolderDto(folder.Id, folder.Path, folder.LastScannedAt);
    }

    public async Task RemoveFolder(uint id, CancellationToken cancellationToken)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        await context.MonitoredFolders
            .Where(f => f.Id == id)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
