using Plaat.Contract.Models.Authorization;

namespace Plaat.Mobile.Services.Auth;

/// <summary>
/// MAUI implementation of <see cref="ITokenSessionStore"/>: keeps the access token in-memory only
/// (via <see cref="TokenStorageService"/>, 30 min lifetime, cheap to re-derive on cold start) and
/// persists just the refresh token to the platform secure storage so it survives an app restart.
/// </summary>
public sealed class AppSecureTokenSessionStore(TokenStorageService tokenStorage) : ITokenSessionStore
{
    private const string RefreshTokenKey = "plaat_refresh_token";

    public Task PersistRefreshedToken(TokenResponse tokenResponse, CancellationToken cancellationToken)
    {
        tokenStorage.AccessToken = tokenResponse.AccessToken;
        tokenStorage.RefreshToken = tokenResponse.RefreshToken;
        tokenStorage.ExpiresAt = tokenResponse.ExpiresAt;

        return SecureStorage.Default.SetAsync(RefreshTokenKey, tokenResponse.RefreshToken);
    }

    public static Task<string?> LoadStoredRefreshToken()
    {
        return SecureStorage.Default.GetAsync(RefreshTokenKey);
    }

    public static void ClearStoredRefreshToken()
    {
        SecureStorage.Default.Remove(RefreshTokenKey);
    }
}
