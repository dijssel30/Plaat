namespace Plaat.Contract.Models.Authorization;

/// <summary>
/// Persists a refreshed access/refresh token pair to wherever the current session keeps them.
/// </summary>
public interface ITokenSessionStore
{
    /// <summary>
    /// Persists a newly refreshed token pair as the current session's tokens.
    /// </summary>
    Task PersistRefreshedToken(TokenResponse tokenResponse, CancellationToken cancellationToken);
}
