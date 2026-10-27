using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Plaat.Contract.Models.Authorization;
using Plaat.Db.Models;

namespace Plaat.Db.Repositories;

/// <summary>
/// Data access for user accounts, password hashes and refresh-token issuance/rotation.
/// </summary>
public interface IUserRepository
{
    /// <summary>
    /// Get hashed password for given username. Returns null if user does not exist.
    /// </summary>
    Task<string?> GetPasswordHash(
        string username,
        CancellationToken cancellationToken);

    /// <summary>
    /// Update hashed password if it needs updating.
    /// </summary>
    Task UpdateHashedPassword(
        string username,
        string passwordHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a refresh token for the user.
    /// </summary>
    Task<RequestRefreshTokenResult> CreateRefreshToken(
        string username,
        CancellationToken cancellationToken);

    /// <summary>
    /// Validates a refresh token and, if valid, rotates it: the presented token is revoked
    /// and a new one is issued in its place. Returns null if the token is unknown or expired.
    /// <para>
    /// Presenting an already-rotated token within <see cref="UserRepository.RotationGracePeriod"/>
    /// of its rotation is treated as a benign replay - two tabs refreshing at once, or a client
    /// that never received the response because the API was restarting - and is answered with a
    /// fresh token in the same family. Later than that, it is treated as theft and revokes the
    /// rest of that family (only that family: other devices keep their sessions).
    /// </para>
    /// </summary>
    Task<RefreshTokenRotationResult?> RotateRefreshToken(
        string refreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active refresh token of the user, forcing all their sessions to sign in again
    /// once their access token expires. Does nothing if the user does not exist.
    /// </summary>
    Task RevokeAllRefreshTokens(
        string username,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get user by ID.
    /// </summary>
    Task<UserSummaryDto?> GetById(
        int id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Create a new user. Returns false if a user with that name (case-insensitive) already exists.
    /// </summary>
    Task<bool> CreateUser(
        string username,
        string passwordHash,
        bool isAdmin,
        CancellationToken cancellationToken);

    /// <summary>
    /// Get a list of all users.
    /// </summary>
    Task<IReadOnlyCollection<UserSummaryDto>> GetAll(
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns false if the user was not found.
    /// </summary>
    Task<bool> SetIsAdmin(
        string username,
        bool isAdmin,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the currently-authenticated user's own account info and settings by ID.
    /// </summary>
    Task<CurrentUserDto?> GetCurrentUser(
        int id,
        CancellationToken cancellationToken);

    /// <summary>
    /// Enables or disables loudness normalization for a user. Returns false if the user was not found.
    /// </summary>
    Task<bool> SetLoudnessNormalizationEnabled(
        string username,
        bool enabled,
        CancellationToken cancellationToken);
}

public sealed record RequestRefreshTokenResult(string RefreshToken, DateTime ExpiresAt);

public sealed record RefreshTokenRotationResult(string Username, string RefreshToken, DateTime ExpiresAt);


internal sealed class UserRepository(IDbContextFactory<PlaatContext> contextFactory) : IUserRepository
{
    /// <summary>
    /// How long after a token is rotated the old one is still answered instead of being treated as
    /// theft. It has to comfortably cover two clients refreshing at the same moment and a client
    /// retrying a refresh whose response was lost when the API restarted, while staying far too
    /// short to be of use to someone replaying a token they captured earlier.
    /// </summary>
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(60);

    public async Task<string?> GetPasswordHash(
        string username,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context
            .Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        return user?.PasswordHash;
    }

    public async Task UpdateHashedPassword(
        string username,
        string hashedPassword,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context
            .Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is not null)
        {
            user.PasswordHash = hashedPassword;

            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<RequestRefreshTokenResult> CreateRefreshToken(
        string username,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context
            .Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            throw new Exception("User not found");
        }

        // A login always starts its own family, so signing in on a second device can never
        // interfere with the session already running on the first.
        var issued = IssueToken(context, user.Id, Guid.NewGuid().ToString("N"));

        await context.SaveChangesAsync(cancellationToken);

        return new RequestRefreshTokenResult(issued.PlainTextToken, issued.ExpiresAt);
    }

    public async Task<RefreshTokenRotationResult?> RotateRefreshToken(
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var tokenHash = HashToken(refreshToken);

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var existingToken = await context
            .RefreshTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

        if (existingToken is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;

        if (existingToken.IsRevoked)
        {
            if (!IsBenignReplay(existingToken, now))
            {
                // Either a token that was deliberately revoked (password change), or one replayed
                // long after it was superseded - the signature of a stolen token. Kill the family
                // it belongs to. Scoped to the family on purpose: the same account signed in on
                // another device is a different family and keeps working.
                var activeFamilyTokens = await context
                    .RefreshTokens
                    .Where(t => t.FamilyId == existingToken.FamilyId && !t.IsRevoked)
                    .ToListAsync(cancellationToken);

                foreach (var token in activeFamilyTokens)
                {
                    token.IsRevoked = true;
                    token.RevokedAt = now;
                }

                await context.SaveChangesAsync(cancellationToken);

                return null;
            }

            // A benign replay: this token was rotated moments ago and the caller is still holding
            // it, because a sibling tab won the race or the response never made it back before the
            // API restarted. The successor is stored only as a hash, so it cannot be handed back;
            // issue another token in the same family instead. Both callers end up with a working
            // token, and the family stays intact.
            var replayIssued = IssueToken(context, existingToken.UserId, existingToken.FamilyId);

            await context.SaveChangesAsync(cancellationToken);

            return new RefreshTokenRotationResult(
                existingToken.User.Username,
                replayIssued.PlainTextToken,
                replayIssued.ExpiresAt);
        }

        if (existingToken.ExpiresAt < now)
        {
            return null;
        }

        var issued = IssueToken(context, existingToken.UserId, existingToken.FamilyId);

        existingToken.IsRevoked = true;
        existingToken.RevokedAt = now;

        // Marks this as "superseded by rotation" rather than "killed", which is what
        // IsBenignReplay keys off. Set before the save, so the revocation and the successor it
        // points at become visible together.
        existingToken.ReplacedByTokenHash = issued.Token.TokenHash;

        await context.SaveChangesAsync(cancellationToken);

        return new RefreshTokenRotationResult(
            existingToken.User.Username,
            issued.PlainTextToken,
            issued.ExpiresAt);
    }

    /// <summary>
    /// True when a revoked token was superseded by an ordinary rotation that happened very
    /// recently - the race this grace period exists to absorb. A token revoked any other way has
    /// no successor recorded, so it never qualifies however recent it is.
    /// </summary>
    private static bool IsBenignReplay(RefreshToken token, DateTime now)
    {
        return token.ReplacedByTokenHash is not null
            && token.RevokedAt is { } revokedAt
            && now - revokedAt <= RotationGracePeriod;
    }

    private static IssuedToken IssueToken(PlaatContext context, int userId, string familyId)
    {
        var plainTextToken = GenerateRefreshToken();
        var expiresAt = GetRefreshTokenExpiry();

        var token = new RefreshToken
        {
            TokenHash = HashToken(plainTextToken),
            UserId = userId,
            FamilyId = familyId,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        context.RefreshTokens.Add(token);

        return new IssuedToken(token, plainTextToken, expiresAt);
    }

    private readonly record struct IssuedToken(RefreshToken Token, string PlainTextToken, DateTime ExpiresAt);

    public async Task RevokeAllRefreshTokens(
        string username,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        // Deliberately account-wide, not family-scoped: this backs a password change, where the
        // point is to end every session everywhere. No successor is recorded, so none of these
        // tokens can be replayed through the rotation grace period.
        await context
            .RefreshTokens
            .Where(t => !t.IsRevoked && t.User.Username == username)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsRevoked, true)
                    .SetProperty(t => t.RevokedAt, DateTime.UtcNow),
                cancellationToken);
    }

    private static string GenerateRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(512));
    }

    private static string HashToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    private static DateTime GetRefreshTokenExpiry()
    {
        return DateTime.UtcNow.AddDays(30);
    }

    public async Task<UserSummaryDto?> GetById(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context
            .Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserSummaryDto(u.Id, u.Username, u.IsAdmin))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> CreateUser(
        string username,
        string passwordHash,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        context.Add(new User
        {
            Username = username,
            PasswordHash = passwordHash,
            IsAdmin = isAdmin
        });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Unique index on Username (NOCASE) - a concurrent or differently-cased duplicate.
            return false;
        }

        return true;
    }

    public async Task<IReadOnlyCollection<UserSummaryDto>> GetAll(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context
            .Users
            .AsNoTracking()
            .Select(u => new UserSummaryDto(u.Id, u.Username, u.IsAdmin))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<bool> SetIsAdmin(string username, bool isAdmin, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context
            .Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            return false;
        }

        user.IsAdmin = isAdmin;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<CurrentUserDto?> GetCurrentUser(int id, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        return await context
            .Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new CurrentUserDto(u.Username, u.IsAdmin, u.LoudnessNormalizationEnabled))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SetLoudnessNormalizationEnabled(string username, bool enabled, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);

        var user = await context
            .Users
            .FirstOrDefaultAsync(u => u.Username == username, cancellationToken);

        if (user is null)
        {
            return false;
        }

        user.LoudnessNormalizationEnabled = enabled;

        await context.SaveChangesAsync(cancellationToken);

        return true;
    }
}
