using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Plaat.Infrastructure.Discogs.Models;

namespace Plaat.Infrastructure.Discogs;

internal sealed partial class DiscogsClient(
    HttpClient httpClient,
    DiscogsOptions options) : IDiscogsClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly DiscogsRateLimiter _rateLimiter = new(options);

    // Discogs disambiguates same-named artists by appending " (n)" to the title,
    // e.g. "Sam Smith (12)". Results come back ordered by relevance/popularity, so
    // stripping the suffix before matching and taking the first hit picks the most
    // popular exact match rather than requiring the raw title (with its arbitrary
    // number) to equal the local artist name.
    [GeneratedRegex(@"\s\(\d+\)$")]
    private static partial Regex DisambiguationSuffixRegex();

    private static string StripDisambiguationSuffix(string title)
    {
        return DisambiguationSuffixRegex().Replace(title.Trim(), string.Empty);
    }

    public async Task<DiscogsSearchArtist?> SearchArtistByExactName(string name, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(name);

        using var response = await SendAsync($"database/search?q={query}&type=artist", cancellationToken);

        if (response is null)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<DiscogsSearchResponseJson>(JsonOptions, cancellationToken);

        var trimmedName = name.Trim();

        var match = payload?.Results?.FirstOrDefault(r =>
            string.Equals(r.Type, "artist", StringComparison.OrdinalIgnoreCase)
            && r.Title is not null
            && string.Equals(StripDisambiguationSuffix(r.Title), trimmedName, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? null
            : new DiscogsSearchArtist(match.Id, StripDisambiguationSuffix(match.Title!));
    }

    public async Task<DiscogsArtistDetails?> GetArtistById(int discogsArtistId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync($"artists/{discogsArtistId}", cancellationToken);

        if (response is null)
        {
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<DiscogsArtistResponseJson>(JsonOptions, cancellationToken);

        if (payload?.Name is null)
        {
            return null;
        }

        var image = payload.Images?.FirstOrDefault(i => string.Equals(i.Type, "primary", StringComparison.OrdinalIgnoreCase))
            ?? payload.Images?.FirstOrDefault();

        return new DiscogsArtistDetails(payload.Id, payload.Name, payload.Profile, image?.Uri);
    }

    public async Task<string?> ResolveReferenceName(char referenceType, int discogsId, CancellationToken cancellationToken)
    {
        var (path, field) = referenceType switch
        {
            'a' => ($"artists/{discogsId}", "name"),
            'l' => ($"labels/{discogsId}", "name"),
            'm' => ($"masters/{discogsId}", "title"),
            'r' => ($"releases/{discogsId}", "title"),
            _ => (null, null)
        };

        if (path is null)
        {
            return null;
        }

        using var response = await SendAsync(path, cancellationToken);

        if (response is null)
        {
            return null;
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

        return document.RootElement.TryGetProperty(field!, out var value)
            ? value.GetString()
            : null;
    }

    /// <summary>
    /// Sends a rate-limited GET request. Returns null on 404. Retries once after the
    /// server-specified delay on 429, then throws if still rate limited.
    /// </summary>
    private async Task<HttpResponseMessage?> SendAsync(
        string requestUri,
        CancellationToken cancellationToken,
        bool isRetry = false)
    {
        await _rateLimiter.WaitForPermitAsync(cancellationToken);

        var response = await httpClient.GetAsync(requestUri, cancellationToken);

        if (response.StatusCode is HttpStatusCode.NotFound)
        {
            response.Dispose();
            return null;
        }

        if (response.StatusCode is HttpStatusCode.TooManyRequests && !isRetry)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(60);
            response.Dispose();

            await Task.Delay(retryAfter, cancellationToken);

            return await SendAsync(requestUri, cancellationToken, isRetry: true);
        }

        response.EnsureSuccessStatusCode();

        return response;
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }
}
