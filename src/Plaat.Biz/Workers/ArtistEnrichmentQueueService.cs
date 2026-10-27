using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Plaat.Biz.Options;
using Plaat.Db.Models;
using Plaat.Db.Repositories;
using Plaat.Infrastructure.Discogs;
using Channel = System.Threading.Channels.Channel;

namespace Plaat.Biz.Workers;

/// <summary>
/// Lazily enriches artists with a summary and image from Discogs, processed one at a time on a
/// background loop so page loads never block on (rate-limited) Discogs API calls.
/// </summary>
public partial class ArtistEnrichmentQueueService : IDisposable
{
    private readonly IArtistRepository _artistRepository;
    private readonly IDiscogsClient _discogsClient;
    private readonly DiscogsProfileFormatter _profileFormatter;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PlaatOptions _options;
    private readonly ILogger<ArtistEnrichmentQueueService> _logger;
    private readonly Channel<ArtistEnrichmentJob> _jobs = Channel.CreateUnbounded<ArtistEnrichmentJob>();
    // ReSharper disable once NotAccessedField.Local
    // Task.Run will keep running from constructor
    private readonly Task _processingLoop;

    public ArtistEnrichmentQueueService(
        IArtistRepository artistRepository,
        IDiscogsClient discogsClient,
        IHttpClientFactory httpClientFactory,
        PlaatOptions options,
        ILogger<ArtistEnrichmentQueueService> logger)
    {
        _artistRepository = artistRepository;
        _discogsClient = discogsClient;
        _profileFormatter = new DiscogsProfileFormatter(discogsClient);
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _processingLoop = Task.Run(() => ProcessJobsAsync(CancellationToken.None));
    }

    private bool Enabled
    {
        get
        {
            return _options.DiscogsSettings?.Enabled == true;
        }
    }

    /// <summary>
    /// Claims the artist for a lookup if the feature is enabled and no lookup has been attempted
    /// yet, enqueues it (still serialized behind any earlier queued jobs, to respect Discogs rate
    /// limits), and waits for that job to finish. Returns whether enrichment actually ran.
    /// </summary>
    public async Task<bool> EnrichAndWait(uint artistId, CancellationToken cancellationToken)
    {
        if (!Enabled)
        {
            return false;
        }

        var claimed = await _artistRepository.TryClaimForEnrichment(artistId, cancellationToken);

        if (!claimed)
        {
            return false;
        }

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await _jobs.Writer.WriteAsync(new ArtistEnrichmentJob(artistId, null, completion), cancellationToken);

        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// Forces a fresh name-based lookup, even if a previous attempt found nothing.
    /// </summary>
    public async Task EnqueueForRefresh(uint artistId, CancellationToken cancellationToken)
    {
        await _artistRepository.ResetToNotAttempted(artistId, cancellationToken);
        await _artistRepository.TryClaimForEnrichment(artistId, cancellationToken);

        await _jobs.Writer.WriteAsync(new ArtistEnrichmentJob(artistId, null), cancellationToken);
    }

    /// <summary>
    /// Forces enrichment from a specific Discogs artist ID, bypassing the exact-name-match check
    /// (the admin has already resolved the identity manually).
    /// </summary>
    public async Task EnqueueForced(uint artistId, int discogsArtistId, CancellationToken cancellationToken)
    {
        await _artistRepository.ResetToNotAttempted(artistId, cancellationToken);
        await _artistRepository.TryClaimForEnrichment(artistId, cancellationToken);

        await _jobs.Writer.WriteAsync(new ArtistEnrichmentJob(artistId, discogsArtistId), cancellationToken);
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        await foreach (var job in _jobs.Reader.ReadAllAsync(ct))
        {
            try
            {
                await ProcessJob(job, ct);
                job.Completion?.TrySetResult(true);
            }
            catch (Exception ex)
            {
                LogEnrichmentFailed(ex, job.ArtistId);

                try
                {
                    await _artistRepository.ResetToNotAttempted(job.ArtistId, ct);
                }
                catch (Exception resetEx)
                {
                    LogResetFailed(resetEx, job.ArtistId);
                }

                job.Completion?.TrySetResult(false);
            }
        }
    }

    private async Task ProcessJob(ArtistEnrichmentJob job, CancellationToken ct)
    {
        var name = await _artistRepository.GetArtistName(job.ArtistId, ct);

        if (name is null)
        {
            return;
        }

        int discogsArtistId;
        DiscogsArtistDetails? details;

        if (job.ForcedDiscogsId is { } forcedId)
        {
            discogsArtistId = forcedId;
            details = await _discogsClient.GetArtistById(forcedId, ct);

            if (details is null)
            {
                LogForcedIdNotFound(forcedId, job.ArtistId);
                await _artistRepository.ResetToNotAttempted(job.ArtistId, ct);
                return;
            }
        }
        else
        {
            var match = await _discogsClient.SearchArtistByExactName(name, ct);

            if (match is null)
            {
                await _artistRepository.SaveEnrichmentResult(
                    job.ArtistId, null, null, null, ArtistDiscogsLookupStatus.NotFound, ct);
                return;
            }

            discogsArtistId = match.Id;
            details = await _discogsClient.GetArtistById(match.Id, ct);

            if (details is null)
            {
                // Found in search but the artist page vanished before we could fetch it - treat as transient.
                await _artistRepository.ResetToNotAttempted(job.ArtistId, ct);
                return;
            }
        }

        var imagePath = details.ImageUri is null
            ? null
            : await DownloadImage(details.ImageUri, ct);

        var summary = details.Profile is null
            ? null
            : await _profileFormatter.Format(details.Profile, ct);

        await _artistRepository.SaveEnrichmentResult(
            job.ArtistId, discogsArtistId, summary, imagePath, ArtistDiscogsLookupStatus.Found, ct);
    }

    private async Task<string?> DownloadImage(string imageUri, CancellationToken ct)
    {
        using var client = _httpClientFactory.CreateClient();

        using var response = await client.GetAsync(imageUri, ct);

        if (!response.IsSuccessStatusCode)
        {
            LogImageDownloadFailed(imageUri, response.StatusCode);
            return null;
        }

        var extension = Path.GetExtension(new Uri(imageUri).AbsolutePath);

        if (string.IsNullOrEmpty(extension))
        {
            extension = ".jpg";
        }

        var fileName = $"{Guid.NewGuid()}{extension}";
        var filePath = Path.Join(_options.ImagesStoragePath, fileName);

        await using var fileStream = File.OpenWrite(filePath);

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);

        await responseStream.CopyToAsync(fileStream, 1024 * 64, ct);

        return fileName;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        _jobs.Writer.TryComplete();
    }

    private record ArtistEnrichmentJob(uint ArtistId, int? ForcedDiscogsId, TaskCompletionSource<bool>? Completion = null);

    [LoggerMessage(LogLevel.Error, "Discogs enrichment failed for artist {ArtistId}. Will retry on next view")]
    private partial void LogEnrichmentFailed(Exception ex, uint artistId);

    [LoggerMessage(LogLevel.Error, "Failed to reset enrichment status for artist {ArtistId}")]
    private partial void LogResetFailed(Exception ex, uint artistId);

    [LoggerMessage(LogLevel.Warning, "Forced Discogs artist ID {DiscogsArtistId} for artist {ArtistId} does not exist")]
    private partial void LogForcedIdNotFound(int discogsArtistId, uint artistId);

    [LoggerMessage(LogLevel.Warning, "Failed to download Discogs artist image from {ImageUri}: {StatusCode}")]
    private partial void LogImageDownloadFailed(string imageUri, System.Net.HttpStatusCode statusCode);
}
