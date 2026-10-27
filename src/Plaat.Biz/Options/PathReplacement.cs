namespace Plaat.Biz.Options;

/// <summary>
/// A single path prefix replacement rule.
/// </summary>
public sealed class PathReplacement
{
    /// <summary>
    /// Path prefix to replace, matched case-insensitively.
    /// </summary>
    public required string From { get; init; }

    /// <summary>
    /// Value the matched prefix is replaced with.
    /// </summary>
    public required string To { get; init; }
}
