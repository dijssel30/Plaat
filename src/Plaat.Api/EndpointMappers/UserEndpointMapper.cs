using System.Net.Mime;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Plaat.Contract.Models.Authorization;
using Plaat.Db.Repositories;

namespace Plaat.Api.EndpointMappers;

public static class UserEndpointMapper
{
    public static void MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var apiGroup = app
            .MapGroup("/api/users")
            .WithTags("Users")
            .RequireAuthorization();

        apiGroup.MapGet(string.Empty, GetUsers)
            .WithSummary("List all users")
            .Produces<IReadOnlyCollection<UserSummaryDto>>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapPost(string.Empty, CreateUser)
            .WithSummary("Create a new user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapGet("me", GetCurrentUser)
            .WithSummary("Get the currently signed in user's own account info and settings")
            .Produces<CurrentUserDto>(contentType: MediaTypeNames.Application.Json)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("me/password", ChangeOwnPassword)
            .WithSummary("Change the password of the currently signed in user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("me/loudness-normalization", SetLoudnessNormalization)
            .WithSummary("Enable or disable loudness normalization for the currently signed in user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        apiGroup.MapPut("{username}/password", SetUserPassword)
            .WithSummary("Set the password of another user")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.AdminPolicy);

        apiGroup.MapPut("{username}/admin", SetUserAdmin)
            .WithSummary("Grant or revoke another user's admin role")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(Policies.AdminPolicy);
    }

    private static async Task<IReadOnlyCollection<UserSummaryDto>> GetUsers(
        [FromServices] IUserRepository userRepository,
        CancellationToken ct)
    {
        return await userRepository.GetAll(ct);
    }

    private static async Task<IResult> CreateUser(
        [FromBody] CreateUserRequest request,
        [FromServices] IPasswordHasher<string> passwordHasher,
        [FromServices] IUserRepository userRepository,
        CancellationToken ct)
    {
        var username = request.Username.Trim();

        if (username.Length < 3)
        {
            return Results.BadRequest("Username must be at least 3 characters.");
        }

        var passwordHash = passwordHasher.HashPassword(username, request.Password);

        var created = await userRepository.CreateUser(username, passwordHash, request.IsAdmin, ct);

        return created
            ? Results.Ok()
            : Results.Conflict();
    }

    private static async Task<IResult> GetCurrentUser(
        [FromServices] IUserRepository userRepository,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken ct)
    {
        var user = await userRepository.GetCurrentUser(claimsPrincipal.GetUserId(), ct);

        return user is null
            ? Results.Unauthorized()
            : Results.Ok(user);
    }

    private static async Task<IResult> SetLoudnessNormalization(
        [FromBody] SetLoudnessNormalizationRequest request,
        [FromServices] IUserRepository userRepository,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken ct)
    {
        var user = await userRepository.GetCurrentUser(claimsPrincipal.GetUserId(), ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        await userRepository.SetLoudnessNormalizationEnabled(user.Username, request.Enabled, ct);

        return Results.Ok();
    }

    private static async Task<IResult> ChangeOwnPassword(
        [FromBody] ChangePasswordRequest request,
        [FromServices] IPasswordHasher<string> passwordHasher,
        [FromServices] IUserRepository userRepository,
        ClaimsPrincipal claimsPrincipal,
        CancellationToken ct)
    {
        var user = await userRepository.GetById(claimsPrincipal.GetUserId(), ct);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        var currentHash = await userRepository.GetPasswordHash(user.Username, ct);

        if (currentHash is null)
        {
            return Results.Unauthorized();
        }

        var result = passwordHasher.VerifyHashedPassword(
            user.Username,
            currentHash,
            request.CurrentPassword);

        if (result is PasswordVerificationResult.Failed)
        {
            return Results.Unauthorized();
        }

        var newHash = passwordHasher.HashPassword(user.Username, request.NewPassword);

        await userRepository.UpdateHashedPassword(user.Username, newHash, ct);
        await userRepository.RevokeAllRefreshTokens(user.Username, ct);

        return Results.Ok();
    }

    private static async Task<IResult> SetUserPassword(
        [FromRoute] string username,
        [FromBody] SetPasswordRequest request,
        [FromServices] IPasswordHasher<string> passwordHasher,
        [FromServices] IUserRepository userRepository,
        CancellationToken ct)
    {
        var existingHash = await userRepository.GetPasswordHash(username, ct);

        if (existingHash is null)
        {
            return Results.NotFound();
        }

        var newHash = passwordHasher.HashPassword(username, request.NewPassword);

        await userRepository.UpdateHashedPassword(username, newHash, ct);
        await userRepository.RevokeAllRefreshTokens(username, ct);

        return Results.Ok();
    }

    private static async Task<IResult> SetUserAdmin(
        [FromRoute] string username,
        [FromBody] SetAdminRequest request,
        [FromServices] IUserRepository userRepository,
        ClaimsPrincipal user,
        CancellationToken ct)
    {
        if (!request.IsAdmin)
        {
            var caller = await userRepository.GetById(user.GetUserId(), ct);

            if (caller is not null && string.Equals(caller.Username, username, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest("You cannot revoke your own admin role.");
            }
        }

        var updated = await userRepository.SetIsAdmin(username, request.IsAdmin, ct);

        if (!updated)
        {
            return Results.NotFound();
        }

        if (!request.IsAdmin)
        {
            await userRepository.RevokeAllRefreshTokens(username, ct);
        }

        return Results.Ok();
    }
}
