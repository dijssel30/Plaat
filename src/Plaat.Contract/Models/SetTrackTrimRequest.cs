namespace Plaat.Contract.Models;

public sealed record SetTrackTrimRequest(TimeSpan? TrimStart, TimeSpan? TrimEnd);
