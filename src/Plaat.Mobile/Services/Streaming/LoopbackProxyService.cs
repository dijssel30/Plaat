using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Plaat.Contract;
using Plaat.Mobile.Services.Auth;

namespace Plaat.Mobile.Services.Streaming;

/// <summary>
/// A local loopback HTTP server that injects the Authorization: Bearer header the real API
/// requires for streaming/cover-art endpoints, so native MediaElement/Image controls - which can't
/// attach custom headers to the URL they're given - can just point at a plain local URL instead.
///
/// Route-for-route port of Plaat.Frontend/AuthEndpoints.cs's proxy handler, adapted from
/// ASP.NET Core's HttpContext to System.Net.HttpListener (there's no ASP.NET Core host in a MAUI
/// app, and embedding Kestrel just for this would be heavy - HttpListener is fully managed and
/// needs no extra platform setup on Android/iOS/Windows/Mac Catalyst).
///
/// Android has no per-app loopback isolation, so every route lives under a random per-launch secret
/// path segment (http://127.0.0.1:port/{secret}/music/1). Other apps on the device can't guess it,
/// so they can't use this proxy (and the bearer token it injects) to read the library. The URLs are
/// only ever built in-process via the *Url methods below.
/// </summary>
public sealed class LoopbackProxyService(
    IHttpClientFactory httpClientFactory,
    PlaatBackendOptions backendOptions,
    AccessTokenGate accessTokenGate,
    ILogger<LoopbackProxyService> logger)
{
    private const int BasePort = 54732;
    private const int MaxPortAttempts = 10;

    private static readonly Regex MusicRoute = new(@"^/music/(\d+)$", RegexOptions.Compiled);
    private static readonly Regex CoverAlbumRoute = new(@"^/cover/album/(\d+)$", RegexOptions.Compiled);
    private static readonly Regex CoverTrackRoute = new(@"^/cover/track/(\d+)(?:\.jpg)?$", RegexOptions.Compiled);
    private static readonly Regex DiscogsImageRoute = new(@"^/discogs-image/artist/(\d+)$", RegexOptions.Compiled);

    private readonly string _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// The local base URI (e.g. http://127.0.0.1:54732/{secret}/) to build stream/cover URLs against.
    /// </summary>
    private Uri BaseUri { get; set; } = null!;

    public void Start()
    {
        if (_listener is not null)
        {
            return;
        }

        for (var attempt = 0; attempt < MaxPortAttempts; attempt++)
        {
            var port = BasePort + attempt;
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                _listener = listener;
                BaseUri = new Uri($"http://127.0.0.1:{port}/{_secret}/");
                break;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        if (_listener is null)
        {
            throw new InvalidOperationException(
                $"Could not bind the local streaming proxy to any port in [{BasePort}, {BasePort + MaxPortAttempts}).");
        }

        _cts = new CancellationTokenSource();
        _ = Task.Run(() => AcceptLoop(_listener, _cts.Token));

        logger.LogInformation("Loopback streaming proxy listening on {BaseUri}", BaseUri);
    }

    public Uri MusicUrl(uint trackId)
    {
        return new Uri(BaseUri, $"music/{trackId}");
    }

    public Uri AlbumCoverUrl(uint albumId)
    {
        return new Uri(BaseUri, $"cover/album/{albumId}");
    }

    public Uri TrackCoverUrl(uint trackId)
    {
        return new Uri(BaseUri, $"cover/track/{trackId}");
    }

    public Uri ArtistDiscogsImageUrl(uint artistId)
    {
        return new Uri(BaseUri, $"discogs-image/artist/{artistId}");
    }

    private async Task AcceptLoop(HttpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Loopback proxy failed to accept a connection");
                continue;
            }

            _ = HandleRequestSafely(context, cancellationToken);
        }
    }

    private async Task HandleRequestSafely(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            await HandleRequest(context, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Loopback proxy request to {Path} failed", context.Request.Url?.AbsolutePath);

            try
            {
                context.Response.StatusCode = (int)HttpStatusCode.BadGateway;
            }
            catch
            {
                // response may already be partially written; nothing more we can do
            }
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private async Task HandleRequest(HttpListenerContext context, CancellationToken cancellationToken)
    {
        // Force a fresh connection per request. With keep-alive (the default), a long-lived audio
        // stream response and a concurrent, short cover-art fetch can end up sharing/reusing the
        // same local TCP connection; the client then misparses where one response ends and the next
        // begins, which surfaces as a WebException ("Unexpected status line") on the artwork request
        // and can corrupt the still-open audio stream, killing playback a few seconds in.
        context.Response.KeepAlive = false;

        var path = context.Request.Url?.AbsolutePath ?? string.Empty;

        // Constant-time check so the secret can't be recovered through response timing.
        var secretPrefix = $"/{_secret}";
        if (path.Length < secretPrefix.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(path[..secretPrefix.Length]),
                Encoding.ASCII.GetBytes(secretPrefix)))
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            return;
        }

        path = path[secretPrefix.Length..];

        Match match;
        if ((match = MusicRoute.Match(path)).Success)
        {
            await ProxyRequest(context, $"api/music/{match.Groups[1].Value}", forwardRange: true, cancellationToken);
        }
        else if ((match = CoverAlbumRoute.Match(path)).Success)
        {
            await ProxyRequest(context, $"api/cover/album/{match.Groups[1].Value}", forwardRange: false, cancellationToken);
        }
        else if ((match = CoverTrackRoute.Match(path)).Success)
        {
            await ProxyRequest(context, $"api/cover/track/{match.Groups[1].Value}", forwardRange: false, cancellationToken);
        }
        else if ((match = DiscogsImageRoute.Match(path)).Success)
        {
            await ProxyRequest(context, $"api/artists/{match.Groups[1].Value}/discogs-image", forwardRange: false, cancellationToken);
        }
        else
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
        }
    }

    private async Task ProxyRequest(
        HttpListenerContext context,
        string upstreamPath,
        bool forwardRange,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = backendOptions.BaseUri;

        using var request = new HttpRequestMessage(HttpMethod.Get, upstreamPath);

        var accessToken = await accessTokenGate.GetValidAccessToken(cancellationToken);
        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        if (forwardRange)
        {
            var rangeHeader = context.Request.Headers["Range"];
            if (!string.IsNullOrEmpty(rangeHeader))
            {
                request.Headers.TryAddWithoutValidation("Range", rangeHeader);
            }
        }

        // Forward the conditional-request header so cover art can 304 instead of being re-streamed;
        // the native Image control keeps its decoded bitmap and skips the download entirely.
        var ifNoneMatch = context.Request.Headers["If-None-Match"];
        if (!string.IsNullOrEmpty(ifNoneMatch))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        context.Response.StatusCode = (int)upstream.StatusCode;

        if (upstream.Headers.ETag is not null)
        {
            context.Response.Headers["ETag"] = upstream.Headers.ETag.ToString();
        }

        if (upstream.Headers.CacheControl is not null)
        {
            context.Response.Headers["Cache-Control"] = upstream.Headers.CacheControl.ToString();
        }

        if (upstream.StatusCode == HttpStatusCode.NotModified)
        {
            return;
        }

        if (upstream.Content.Headers.ContentType is not null)
        {
            context.Response.ContentType = upstream.Content.Headers.ContentType.ToString();
        }

        if (upstream.Content.Headers.ContentLength.HasValue)
        {
            context.Response.ContentLength64 = upstream.Content.Headers.ContentLength.Value;
        }

        if (forwardRange && upstream.Content.Headers.TryGetValues("Content-Range", out var contentRange))
        {
            context.Response.Headers["Content-Range"] = contentRange.FirstOrDefault();
        }

        if (forwardRange && upstream.Headers.AcceptRanges.Count > 0)
        {
            // Without this, MediaElement has no way to know up front that seeking is supported,
            // since it only sees this proxy's response - not the upstream API's - on first load.
            context.Response.Headers["Accept-Ranges"] = string.Join(", ", upstream.Headers.AcceptRanges);
        }

        await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
        await upstreamStream.CopyToAsync(context.Response.OutputStream, cancellationToken);
    }
}
