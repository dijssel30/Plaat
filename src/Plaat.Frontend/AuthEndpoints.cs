using System.Net.Http.Headers;
using Plaat.Contract;

namespace Plaat.Frontend;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        app.MapGet("/proxy/cover/album/{id:int}", ProxyCoverAlbum);
        app.MapGet("/proxy/cover/track/{id:int}", ProxyCoverTrack);
        app.MapGet("/proxy/music/{id:int}", ProxyMusic);
        app.MapGet("/proxy/discogs-image/artist/{id:int}", ProxyDiscogsImageArtist);
    }

    private static async Task ProxyCoverAlbum(int id, HttpContext ctx, IHttpClientFactory factory,
        PlaatBackendOptions backendOptions, ProxySessionStore sessionStore, CancellationToken ct)
    {
        await ProxyRequest(ctx, factory, backendOptions, sessionStore, $"api/cover/album/{id}", ct);
    }

    private static async Task ProxyCoverTrack(int id, HttpContext ctx, IHttpClientFactory factory,
        PlaatBackendOptions backendOptions, ProxySessionStore sessionStore, CancellationToken ct)
    {
        await ProxyRequest(ctx, factory, backendOptions, sessionStore, $"api/cover/track/{id}", ct);
    }

    private static async Task ProxyMusic(int id, HttpContext ctx, IHttpClientFactory factory,
        PlaatBackendOptions backendOptions, ProxySessionStore sessionStore, CancellationToken ct)
    {
        await ProxyRequest(ctx, factory, backendOptions, sessionStore, $"api/music/{id}", ct, forwardRange: true);
    }

    private static async Task ProxyDiscogsImageArtist(int id, HttpContext ctx, IHttpClientFactory factory,
        PlaatBackendOptions backendOptions, ProxySessionStore sessionStore, CancellationToken ct)
    {
        await ProxyRequest(ctx, factory, backendOptions, sessionStore, $"api/artists/{id}/discogs-image", ct);
    }

    private static async Task ProxyRequest(
        HttpContext ctx,
        IHttpClientFactory factory,
        PlaatBackendOptions backendOptions,
        ProxySessionStore sessionStore,
        string path,
        CancellationToken ct,
        bool forwardRange = false)
    {
        // No usable session: the cookie outlives the store, which is in-process only, so a restart
        // (or an expired token) leaves the browser presenting a session id this process has never
        // heard of. Answer 401 here rather than calling upstream with no Authorization header at
        // all - that produced the same 401 but made it look like the API had rejected a real
        // request, and gave the caller nothing to distinguish "sign in again" from "this track is
        // broken". The circuit re-establishes the session on its next render.
        var accessToken = GetSessionAccessToken(ctx, sessionStore);

        if (accessToken is null)
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;

            return;
        }

        using var client = factory.CreateClient("proxy");
        client.BaseAddress = backendOptions.BaseUri;

        using var request = new HttpRequestMessage(HttpMethod.Get, path);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        if (forwardRange && ctx.Request.Headers.TryGetValue("Range", out var rangeHeader))
        {
            request.Headers.TryAddWithoutValidation("Range", rangeHeader.ToString());
        }

        // Forward the browser's conditional-request header so cover art can 304 through the proxy
        // instead of being re-fetched and re-streamed on every grid scroll / page revisit.
        if (ctx.Request.Headers.TryGetValue("If-None-Match", out var ifNoneMatch))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch.ToString());
        }

        using var upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        ctx.Response.StatusCode = (int)upstream.StatusCode;

        if (upstream.Headers.ETag is not null)
        {
            ctx.Response.Headers.ETag = upstream.Headers.ETag.ToString();
        }

        if (upstream.Headers.CacheControl is not null)
        {
            ctx.Response.Headers.CacheControl = upstream.Headers.CacheControl.ToString();
        }

        if (upstream.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return;
        }

        ctx.Response.ContentType = upstream.Content.Headers.ContentType?.ToString();

        if (upstream.Content.Headers.ContentLength.HasValue)
        {
            ctx.Response.ContentLength = upstream.Content.Headers.ContentLength;
        }

        if (forwardRange && upstream.Content.Headers.TryGetValues("Content-Range", out var contentRange))
        {
            ctx.Response.Headers.ContentRange = contentRange.FirstOrDefault();
        }

        if (forwardRange && upstream.Headers.AcceptRanges.Count > 0)
        {
            // Without this, the browser has no way to know up front that seeking is supported,
            // since it only sees this proxy's response - not the upstream API's - on first load.
            ctx.Response.Headers.AcceptRanges = string.Join(", ", upstream.Headers.AcceptRanges);
        }

        await upstream.Content.CopyToAsync(ctx.Response.Body, ct);
    }

    private static string? GetSessionAccessToken(HttpContext ctx, ProxySessionStore sessionStore)
    {
        if (!ctx.Request.Cookies.TryGetValue(SessionEndpoints.CookieName, out var sessionId))
        {
            return null;
        }

        return sessionStore.GetAccessToken(sessionId);
    }
}
