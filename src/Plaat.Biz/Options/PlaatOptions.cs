namespace Plaat.Biz.Options;

public sealed class PlaatOptions
{
    /// <summary>
    /// Location that the converted webp images will be stored.
    /// </summary>
    public required string ImagesStoragePath { get; init; }

    /// <summary>
    /// Location that the reconverted AAC file will be put if reconversion is needed.
    /// </summary>
    public required string TargetReconvertedPath { get; init; }

    /// <summary>
    /// Location of csv file with pre-calculated hashes. If this file exists and the music file is present in the
    /// list, the music scanner will not calcualte the hash again. The hashes will also be added to the file.
    /// </summary>
    public string? PrecalculatedHashCsvPath { get; init; }

    /// <summary>
    /// Boolean indicating the use of qaac to reconvert aac files instead of ffmpeg.
    /// If set to true and <see cref="QaacPath"/> is null or qaac does not exist, reconverting files will fail.
    /// If set to false, standard ffmpeg AAC converter is used (ffmpeg needs to be installed in path to convert).
    /// NB: if running linux, wine needs to be installed in /usr/bin/wine to run.
    /// </summary>
    public bool UseQaac { get; init; }

    /// <summary>
    /// Location of qaac binary. This needs to be set if <see cref="UseQaac"/> is set to true.
    /// </summary>
    public string? QaacPath { get; init; }

    /// <summary>
    /// Advanced replace options.
    /// </summary>
    public ReplaceOptions? ReplaceOptions { get; init; }

    /// <summary>
    /// Options for jwt token authentication to the backend.
    /// </summary>
    public required JwtOptions JwtOptions { get; init; }

    /// <summary>
    /// Options for the Discogs artist enrichment feature (summary/image lookup). Null or
    /// <see cref="Options.DiscogsSettings.Enabled"/> false disables the feature entirely.
    /// </summary>
    public DiscogsSettings? DiscogsSettings { get; init; }

    /// <summary>
    /// Options for the automatic database backup. Null or <see cref="Options.BackupSettings.Enabled"/>
    /// false disables it.
    /// </summary>
    public BackupSettings? BackupSettings { get; init; }

    /// <summary>
    /// Options for rate limiting the login/token/refresh endpoints. Null uses the defaults.
    /// </summary>
    public RateLimitSettings? RateLimitSettings { get; init; }

    /// <summary>
    /// How many tracks the background loudness-analysis worker measures concurrently. Each one
    /// shells out to ffmpeg, which is CPU-heavy, so this defaults to 1 (no concurrency) to avoid
    /// overloading the machine while a scan runs. Raise it only on a box with cycles to spare.
    /// </summary>
    public int LoudnessAnalysisMaxDegreeOfParallelism { get; init; } = 1;

    /// <summary>
    /// If set to false, all background services will be disabled (default true)
    /// </summary>
    public bool EnableBackgroundServices { get; init; } = true;
}
