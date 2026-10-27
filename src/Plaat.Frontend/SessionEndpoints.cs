using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Plaat.Contract;

namespace Plaat.Frontend;

public static class SessionEndpoints
{
    public const string CookieName = "plaat_session";

    public static void MapSessionEndpoints(this WebApplication app)
    {
        app.MapPost("/session/establish", Establish);
        app.MapPost("/session/clear", Clear);
    }

    private static async Task<IResult> Establish(
        EstablishSessionRequest request,
        HttpContext ctx,
        ProxySessionStore store,
        IHttpClientFactory httpClientFactory,
        PlaatBackendOptions backendOptions,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.AccessToken))
        {
            return Results.BadRequest();
        }

        // The Frontend has no copy of the JWT signing key - only the API validates tokens - so
        // ReadJwtToken below only parses the claims, it doesn't prove they're genuine. Confirm the
        // API itself accepts this exact token before trusting its claims to sign in a cookie session.
        if (!await IsAcceptedByApi(request.AccessToken, httpClientFactory, backendOptions, ct))
        {
            return Results.Unauthorized();
        }

        var existingSessionId = ctx.Request.Cookies[CookieName];
        var sessionId = store.Establish(existingSessionId, request.AccessToken, request.ExpiresAt);

        ctx.Response.Cookies.Append(CookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = ctx.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddDays(30)
        });

        // Mirrors the identity JwtAuthStateProvider builds client-side from this same JWT, so the
        // initial (pre-circuit) HTTP request for an [Authorize] page - e.g. a browser refresh -
        // sees the same authenticated/roles state the circuit already has, instead of always
        // bouncing through /login.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(request.AccessToken);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "jwt"));

        await ctx.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                ExpiresUtc = DateTime.SpecifyKind(request.ExpiresAt, DateTimeKind.Utc),
                IsPersistent = true
            });

        return Results.Ok();
    }

    private static async Task<bool> IsAcceptedByApi(
        string accessToken,
        IHttpClientFactory httpClientFactory,
        PlaatBackendOptions backendOptions,
        CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("proxy");
            client.BaseAddress = backendOptions.BaseUri;

            using var validationRequest = new HttpRequestMessage(HttpMethod.Get, "api/state");
            validationRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(validationRequest, ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    private static async Task<IResult> Clear(HttpContext ctx, ProxySessionStore store)
    {
        var sessionId = ctx.Request.Cookies[CookieName];

        if (sessionId is not null)
        {
            store.Remove(sessionId);
        }

        ctx.Response.Cookies.Delete(CookieName);

        await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

        return Results.Ok();
    }
}

public sealed record EstablishSessionRequest(string AccessToken, DateTime ExpiresAt);
