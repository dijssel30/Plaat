namespace Plaat.Contract.Models;

public sealed record MonitoredFolderDto(uint Id, string Path, DateTimeOffset? LastScannedAt);
