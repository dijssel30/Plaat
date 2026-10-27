namespace Plaat.Contract.Loudness;

/// <summary>
/// Converts a track's measured integrated loudness into an attenuate-only playback gain,
/// shared by DB projections (Plaat.Db) and both clients (Plaat.Frontend, Plaat.Mobile).
/// </summary>
public static class LoudnessNormalization
{
    /// <summary>
    /// Target integrated loudness in LUFS, matching Spotify/YouTube/Apple Music.
    /// </summary>
    public const double DefaultTargetLufs = -14.0;

    /// <summary>
    /// Computes the linear gain multiplier to apply on top of the user's own volume so a track's
    /// measured loudness matches <paramref name="targetLufs"/>. Never exceeds 1.0 - quiet tracks
    /// are never boosted, only loud tracks are turned down.
    /// </summary>
    public static double ComputeAttenuationFactor(float? integratedLoudnessLufs, double targetLufs = DefaultTargetLufs)
    {
        if (integratedLoudnessLufs is not { } lufs)
        {
            return 1.0;
        }

        var factor = Math.Pow(10, (targetLufs - lufs) / 20.0);

        return Math.Clamp(factor, 0.0, 1.0);
    }
}
