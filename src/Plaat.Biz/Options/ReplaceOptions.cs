namespace Plaat.Biz.Options;

/// <summary>
/// Options for replacing paths while scanning for music. 
/// </summary>
public sealed class ReplaceOptions
{
    /// <summary>
    /// List of path prefixes to replace.
    /// </summary>
    public required IReadOnlyList<PathReplacement> ReplacePaths { get; init; }
}
