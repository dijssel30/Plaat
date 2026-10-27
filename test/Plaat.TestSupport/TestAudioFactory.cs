namespace Plaat.TestSupport;

/// <summary>
/// Generates tiny, real, decodable audio/image fixture files on disk for E2E/integration tests.
/// Plaat.Api's music-streaming endpoint (<c>MusicEndpointMapper.PlayMusic</c>) reads bytes straight
/// off disk at a track's stored <c>TrackLocation.Path</c>, so a seeded track needs a real file there
/// - not just a DB row - for streaming, Range requests, and actual browser playback to work.
/// </summary>
public static class TestAudioFactory
{
    /// <summary>
    /// Writes a quiet, mono, 8kHz, 16-bit PCM WAV file of the given duration - a low-amplitude sine
    /// tone, not true digital silence. WAV needs no codec support beyond what every browser already
    /// ships, and is trivial to synthesize deterministically without depending on ffmpeg or any
    /// bundled binary asset.
    /// <para>
    /// Deliberately not pure silence: Plaat.Api's background loudness analyzer
    /// (<c>LoudnessAnalysisBackgroundService</c>) measures every newly-scanned track, and integrated
    /// loudness of true digital silence is <c>-Infinity</c> LUFS by definition (EBU R128) - which
    /// then fails to JSON-serialize (<c>System.Text.Json</c> rejects non-finite floats) the moment
    /// any endpoint returns that track, a 500 that's surprisingly easy to hit with an all-zero test
    /// fixture. A quiet-but-real tone measures as some large-but-finite negative LUFS instead.
    /// </para>
    /// </summary>
    public static string CreateSilentWavFile(string path, TimeSpan duration)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        const int sampleRate = 8000;
        const short bitsPerSample = 16;
        const short channels = 1;
        const double toneHz = 440.0;
        const short amplitude = 200; // quiet relative to the full 16-bit range, but not zero

        var sampleCount = (int)(duration.TotalSeconds * sampleRate);
        var dataSize = sampleCount * channels * (bitsPerSample / 8);

        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());

        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8)); // byte rate
        writer.Write((short)(channels * (bitsPerSample / 8))); // block align
        writer.Write(bitsPerSample);

        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(amplitude * Math.Sin(2 * Math.PI * toneHz * i / sampleRate));
            writer.Write(sample);
        }

        return path;
    }

    /// <summary>
    /// Writes a minimal-but-valid 1x1 JPEG so file-existence/size checks in cover-art paths see a real image.
    /// </summary>
    public static string CreateTinyJpegFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, TinyJpegBytes);
        return path;
    }

    // Smallest valid 1x1 white JPEG (base64 decoded once at startup).
    private static readonly byte[] TinyJpegBytes = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgICAgMCAgIDAwMDBAYEBAQEBAgGBgUGCQgKCgkICQkKDA8MCgsOCwkJDRENDg8QEBEQCgwSExIQEw8QEBD/2wBDAQMDAwQDBAgEBAgQCwkLEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBD/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAj/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=");
}
