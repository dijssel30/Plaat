using System.Globalization;
using System.Net.Mime;
using System.Security.Cryptography;
using System.Text;
using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plaat.Biz.MediaInfo;
using Plaat.Biz.Options;
using Plaat.Db;
using Plaat.Db.Models;
using File = TagLib.File;

namespace Plaat.Biz.Scanners;

/// <summary>
/// Stateful helper that creates or resolves Track/Album/Artist/Genre entities from audio files.
/// Must be reset between independent scans via <see cref="Reset"/>.
/// </summary>
internal partial class TrackFactory(PlaatOptions options, MediaInfoParser mediaInfoParser, ILogger<TrackFactory> logger)
{
    private readonly Dictionary<string, Genre> _genresCreated =
        new(StringComparer.InvariantCultureIgnoreCase);
    private readonly Dictionary<string, Artist> _artistsCreated =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Album>> _albumsCreatedByTitle =
        new(StringComparer.InvariantCultureIgnoreCase);

    public void Reset()
    {
        _genresCreated.Clear();
        _albumsCreatedByTitle.Clear();
        _artistsCreated.Clear();
    }

    /// <summary>
    /// Pre-loads all genres and albums from the DB and artists via EF local cache.
    /// </summary>
    public async Task PreloadAsync(PlaatContext context, CancellationToken ct)
    {
        await foreach (var genre in context.Genres.AsAsyncEnumerable().WithCancellation(ct))
        {
            _genresCreated[genre.Name] = genre;
        }

        await foreach (var album in context.Albums
                           .Include(a => a.AlbumArtists)
                           .AsAsyncEnumerable()
                           .WithCancellation(ct))
        {
            AddAlbumToIndex(album);
        }

        await foreach (var artist in context.Artists.AsAsyncEnumerable().WithCancellation(ct))
        {
            _artistsCreated[artist.NormalizedName] = artist;
        }
    }

    private void AddAlbumToIndex(Album album)
    {
        if (!_albumsCreatedByTitle.TryGetValue(album.Title, out var albums))
        {
            albums = [];
            _albumsCreatedByTitle[album.Title] = albums;
        }

        albums.Add(album);
    }

    public async Task<Track> BuildTrackFromFile(
        PlaatContext context,
        string path,
        string fileHash,
        TrackInfo? trackInfo)
    {
        var trackLocation = new TrackLocation { Path = path };

        var track = new Track
        {
            Title = string.Empty,
            FileHash = fileHash,
            MimeType = string.Empty,
            TrackLocations = [trackLocation],
            AddedDate = trackInfo?.AddedDate ?? DateTime.UtcNow,
            PlayCount = trackInfo?.PlayCount ?? 0
        };

        var isSupportedFormat = await ApplyMetadataAsync(track, path, fileHash);
        trackLocation.IsSupportedFormat = isSupportedFormat;

        if (trackInfo?.PlayCount is { } playCount and < int.MaxValue)
        {
            track.Plays.Add(new Play
            {
                Weight = (int)playCount,
                IsImported = true,
                DateTime = DateTime.UtcNow
            });
        }

        context.Add(track);

        if (!isSupportedFormat)
        {
            context.Add(new ConvertQueueItem { TrackLocation = trackLocation });
        }

        return track;
    }

    /// <summary>
    /// Re-reads the tags/media info for a track from the given (already-hashed) file path and applies
    /// them to an existing track, without touching its locations, play history or added date.
    /// </summary>
    /// <returns>Whether the file's audio format is supported for direct playback.</returns>
    public async Task<bool> ReloadTrackMetadataAsync(Track track, string path, string fileHash)
    {
        return await ApplyMetadataAsync(track, path, fileHash);
    }

    private async Task<bool> ApplyMetadataAsync(Track track, string path, string fileHash)
    {
        using var tagLibFile = File.Create(path);
        var mediaInfo = await mediaInfoParser.GetMediaInfoResult(path);

        var title = tagLibFile.Tag.Title;
        var album = tagLibFile.Tag.Album;
        var artists = tagLibFile.Tag.Performers ?? [];
        var year = tagLibFile.Tag.Year;

        var dbArtists = artists
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .Select(GetArtist)
            .ToList();

        var dbAlbum = await GetAlbum(tagLibFile, album);

        var trackNumber = tagLibFile.Tag.Track;
        var discNumber = tagLibFile.Tag.Disc;
        var genres = tagLibFile.Tag.Genres ?? [];

        var mimeType = tagLibFile.MimeType;
        var lyrics = string.IsNullOrWhiteSpace(tagLibFile.Tag.Lyrics) ? null : tagLibFile.Tag.Lyrics;
        var duration = tagLibFile.Properties.Duration;
        var possiblyCorrupt = tagLibFile.PossiblyCorrupt;
        var bitrate = tagLibFile.Properties.AudioBitrate;
        var sampleRate = tagLibFile.Properties.AudioSampleRate;
        var bitsPerSample = tagLibFile.Properties.BitsPerSample;

        var audioTrackInfo = mediaInfo?.Media?.Tracks
            .FirstOrDefault(t => t.Type?.Equals("Audio", StringComparison.InvariantCultureIgnoreCase) is true);

        var generalTrackInfo = mediaInfo?.Media?.Tracks
            .FirstOrDefault(t => t.Type?.Equals("General", StringComparison.InvariantCultureIgnoreCase) is true);

        if (bitsPerSample <= 0 && int.TryParse(audioTrackInfo?.BitDepth, out var mediaInfoBitDepth))
        {
            bitsPerSample = mediaInfoBitDepth;
        }

        if (sampleRate <= 0 && int.TryParse(audioTrackInfo?.SamplingRate, out var mediaInfoSampleRate))
        {
            sampleRate = mediaInfoSampleRate;
        }

        var codec = audioTrackInfo?.Format;
        var originalContentHash = audioTrackInfo?.Extra?.GetValueOrDefault("MD5_Unencoded");

        var recordedDate = ParseRecordedDate(generalTrackInfo?.RecordedDate);
        if (recordedDate is not null)
        {
            year = (uint)recordedDate.Value.Year;
        }

        var isSupportedFormat = IsSupportedFormat(codec);
        var genreEntities = genres.Select(GetGenre).ToList();

        track.Title = title ?? Path.GetFileNameWithoutExtension(path);
        track.Artists = dbArtists;
        track.Album = dbAlbum;
        track.Genres = genreEntities;
        track.TrackNumber = trackNumber;
        track.DiscNumber = discNumber;
        track.Duration = duration;
        track.Year = year;
        track.RecordedDate = recordedDate;
        track.PossiblyCorrupt = possiblyCorrupt;
        track.BitRate = bitrate;
        track.SampleRate = sampleRate;
        track.BitsPerSample = bitsPerSample;
        track.FileHash = fileHash;
        track.OriginalContentHash = originalContentHash;
        track.MimeType = mimeType;
        track.Lyrics = lyrics;
        track.Format = codec;
        track.FileSizeBytes = new FileInfo(path).Length;

        return isSupportedFormat;
    }

    internal static DateOnly? ParseRecordedDate(string? recordedDateText)
    {
        if (recordedDateText is null)
        {
            return null;
        }

        // Strip a trailing time-of-day component (e.g. "2020-05-01 00:00:00" from ID3v2 TDRC tags) —
        // DateOnly.TryParse rejects any string containing a time part, even midnight.
        var datePart = recordedDateText.Split([' ', 'T'], 2)[0];

        return DateOnly.TryParse(datePart, CultureInfo.InvariantCulture, out var parsedRecordedDate)
            ? parsedRecordedDate
            : null;
    }

    private static readonly IReadOnlyCollection<string?> UnsupportedFormats = ["ALAC", "PCM"];

    private static bool IsSupportedFormat(string? format)
    {
        return !UnsupportedFormats.Contains(format, StringComparer.InvariantCultureIgnoreCase);
    }

    private Genre GetGenre(string genre)
    {
        if (_genresCreated.TryGetValue(genre, out var genreEntity))
        {
            return genreEntity;
        }

        genreEntity = new Genre { Name = genre };
        _genresCreated[genre] = genreEntity;
        return genreEntity;
    }

    private Artist GetArtist(string artistName)
    {
        var normalizedName = Normalize(artistName);

        if (_artistsCreated.TryGetValue(normalizedName, out var artist))
        {
            return artist;
        }

        artist = new Artist
        {
            Name = artistName,
            NormalizedName = normalizedName
        };

        _artistsCreated[normalizedName] = artist;
        return artist;
    }

    private static string[] NonBlank(string[]? names)
    {
        return [.. (names ?? []).Where(n => !string.IsNullOrWhiteSpace(n))];
    }

    private async Task<Album> GetAlbum(
        File file,
        string? title)
    {
        var albumArtists = NonBlank(file.Tag.AlbumArtists);

        if (albumArtists.Length is 0)
        {
            albumArtists = NonBlank(file.Tag.Performers); // could also be VA?
        }

        if (albumArtists.Length is 0)
        {
            albumArtists = ["Various Artists"];
        }

        var albumArtistsTagsNormalized = albumArtists
            .Select(Normalize)
            .ToHashSet();

        var resolvedTitle = title ?? "Unknown Album";

        if (_albumsCreatedByTitle.TryGetValue(resolvedTitle, out var candidates))
        {
            var existingAlbum = candidates.FirstOrDefault(a => a.AlbumArtists
                .Select(artist => artist.NormalizedName)
                .ToHashSet()
                .SetEquals(albumArtistsTagsNormalized));

            if (existingAlbum is not null)
            {
                return existingAlbum;
            }
        }

        var dbAlbumArtists = albumArtists
            .Select(GetArtist)
            .ToList();

        var totalTracks = file.Tag.TrackCount;
        var discCount = file.Tag.DiscCount;

        string? coverArtFileName = null;
        string? hash = null;
        var mimeType = MediaTypeNames.Image.Webp;

        var folderCoverPath = FindFolderCover(file.Name);

        if (folderCoverPath is not null)
        {
            // Use the folder's cover.jpg as-is (absolute path, no re-encoding); the cover endpoint
            // serves absolute paths directly.
            try
            {
                hash = await ComputeHashAsync(folderCoverPath, CancellationToken.None);
                coverArtFileName = folderCoverPath;
                mimeType = MediaTypeNames.Image.Jpeg;
            }
            catch (Exception ex)
            {
                LogCoverArtExtractFailed(ex, file.Name);
            }
        }

        if (coverArtFileName is null && file.Tag.Pictures.Length > 0)
        {
            var pic = file.Tag.Pictures.First();
            var data = pic.Data.Data;

            var pictureHash = GetFileHash(data);
            var pictureFileName = $"{pictureHash}.webp";
            var coverArtPath = Path.Join(options.ImagesStoragePath, pictureFileName);

            if (Path.Exists(coverArtPath))
            {
                hash = pictureHash;
                coverArtFileName = pictureFileName;
            }
            else
            {
                try
                {
                    await using var imageStream = new MemoryStream(data);

                    await FFMpegArguments
                        .FromPipeInput(new StreamPipeSource(imageStream))
                        .OutputToFile(coverArtPath, false, o
                            => o.WithCustomArgument("-q:v 80"))
                        .ProcessAsynchronously();

                    hash = pictureHash;
                    coverArtFileName = pictureFileName;
                }
                catch (Exception ex)
                {
                    LogCoverArtExtractFailed(ex, file.Name);
                }
            }
        }

        var album = new Album
        {
            Title = resolvedTitle,
            AlbumArtists = dbAlbumArtists,
            TotalDiscs = discCount,
            TotalTracks = totalTracks,
            CoverArt = coverArtFileName is null
                ? null
                : new CoverArt
                {
                    Hash = hash!,
                    Path = coverArtFileName,
                    MimeType = mimeType
                }
        };

        AddAlbumToIndex(album);
        return album;
    }

    private static string? FindFolderCover(string trackPath)
    {
        var directory = Path.GetDirectoryName(trackPath);

        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        // Linux file systems are case-sensitive, so match "cover.jpg" case-insensitively.
        return Directory.EnumerateFiles(directory)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("cover.jpg", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetFileHash(byte[] data)
    {
        var hash = MD5.HashData(data);
        return Convert.ToHexString(hash);
    }

    public static async Task<string> ComputeHashAsync(string path, CancellationToken ct)
    {
        await using var stream = System.IO.File.OpenRead(path);
        var hash = await MD5.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant().Normalize(NormalizationForm.FormC);
    }

    [LoggerMessage(LogLevel.Warning, "Failed to extract embedded cover art for {file}; importing without cover art")]
    private partial void LogCoverArtExtractFailed(Exception ex, string file);
}
