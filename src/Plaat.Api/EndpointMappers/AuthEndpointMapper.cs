using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Plaat.Biz;
using Plaat.Contract.Models.Authorization;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static partial class AuthEndpointMapper
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/auth")
            .WithTags("Authorization");

        apiGroup.MapPost("login", Login)
            .WithSummary("Validate credentials and return current user info")
            .Produces<TokenResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.Login)
            .AllowAnonymous();

        apiGroup.MapPost("refresh", Refresh)
            .WithSummary("Create new access token from refresh token")
            .Produces<TokenResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.Refresh)
            .AllowAnonymous();

        apiGroup.MapPost("token", Token)
            .WithSummary("OAuth2 password-grant token endpoint used by the Swagger UI Authorize dialog")
            .Accepts<IFormCollection>("application/x-www-form-urlencoded")
            .Produces<OAuthTokenResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireRateLimiting(RateLimitPolicies.Login)
            .AllowAnonymous();
    }

    private static async Task<IResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] IPasswordHasher<string> passwordHasher,
        [FromServices] IUserRepository userRepository,
        [FromServices] TokenService tokenService,
        [FromServices] ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        if (!await VerifyCredentials(request.Username, request.Password, passwordHasher, userRepository, ct))
        {
            LogFailedLogin(logger, request.Username, GetSourceIp(context));

            return Results.Unauthorized();
        }

        var accessTokenExpiry = TokenService.GetAccessTokenExpiry();
        var accessToken = await tokenService.GenerateAccessToken(request.Username, ct);

        var refreshTokenResult = await userRepository.CreateRefreshToken(
            request.Username,
            ct);

        return Results.Ok(new TokenResponse(
            accessToken,
            refreshTokenResult.RefreshToken,
            accessTokenExpiry
        ));
    }

    private static async Task<IResult> Token(
        HttpRequest request,
        [FromServices] IPasswordHasher<string> passwordHasher,
        [FromServices] IUserRepository userRepository,
        [FromServices] TokenService tokenService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var form = await request.ReadFormAsync(ct);
        var username = form["username"].ToString();
        var password = form["password"].ToString();

        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(password)
            || !await VerifyCredentials(username, password, passwordHasher, userRepository, ct))
        {
            LogFailedLogin(logger, username, GetSourceIp(request.HttpContext));

            return Results.Unauthorized();
        }

        var accessTokenExpiry = TokenService.GetAccessTokenExpiry();
        var accessToken = await tokenService.GenerateAccessToken(username, ct);

        return Results.Ok(new OAuthTokenResponse(
            accessToken,
            "Bearer",
            (int)(accessTokenExpiry - DateTime.UtcNow).TotalSeconds
        ));
    }

    /// <summary>
    /// Login requests normally arrive from Plaat.Frontend's own server-side HTTP call, which passes the
    /// browser's IP as X-Forwarded-For. The forwarded-headers middleware (Program.cs) applies that header
    /// to <see cref="ConnectionInfo.RemoteIpAddress"/> only when the caller is a trusted proxy, so the
    /// header is never read directly here (it would be spoofable).
    /// </summary>
    private static string GetSourceIp(HttpContext context)
    {
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private static async Task<bool> VerifyCredentials(
        string username,
        string password,
        IPasswordHasher<string> passwordHasher,
        IUserRepository userRepository,
        CancellationToken ct)
    {
        var hashedPassword = await userRepository.GetPasswordHash(username, ct);

        if (hashedPassword is null)
        {
            return false;
        }

        var result = passwordHasher.VerifyHashedPassword(username, hashedPassword, password);

        if (result is PasswordVerificationResult.Failed)
        {
            return false;
        }

        if (result is PasswordVerificationResult.SuccessRehashNeeded)
        {
            var rehashedPassword = passwordHasher.HashPassword(username, password);

            await userRepository.UpdateHashedPassword(username, rehashedPassword, ct);
        }

        return true;
    }

    private static async Task<IResult> Refresh(
        [FromBody] RefreshRequest request,
        [FromServices] IUserRepository userRepository,
        [FromServices] TokenService tokenService,
        CancellationToken ct)
    {
        var rotationResult = await userRepository.RotateRefreshToken(
            request.RefreshToken,
            ct);

        if (rotationResult is null)
        {
            return Results.Unauthorized();
        }

        var accessTokenExpiry = TokenService.GetAccessTokenExpiry();
        var accessToken = await tokenService.GenerateAccessToken(rotationResult.Username, ct);

        return Results.Ok(new TokenResponse(
            accessToken,
            rotationResult.RefreshToken,
            accessTokenExpiry
        ));
    }

    [LoggerMessage(LogLevel.Warning, "Failed login attempt for user {Username} from {SourceIp}")]
    private static partial void LogFailedLogin(ILogger logger, string username, string sourceIp);
}
