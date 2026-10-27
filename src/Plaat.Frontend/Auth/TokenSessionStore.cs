using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Auth;

public sealed class TokenSessionStore(
    TokenStorageService tokenStorage,
    PersistentTokenStore persistentTokenStore,
    JwtAuthStateProvider authStateProvider,
    IProxySessionSync proxySessionSync) : ITokenSessionStore
{
    public async Task PersistRefreshedToken(TokenResponse tokenResponse, CancellationToken cancellationToken)
    {
        tokenStorage.AccessToken = tokenResponse.AccessToken;
        tokenStorage.ExpiresAt = tokenResponse.ExpiresAt;
        tokenStorage.RefreshToken = tokenResponse.RefreshToken;

        await persistentTokenStore.Save(tokenResponse);

        authStateProvider.NotifyUserAuthenticated();

        await proxySessionSync.Establish(tokenResponse.AccessToken, tokenResponse.ExpiresAt);
    }
}
