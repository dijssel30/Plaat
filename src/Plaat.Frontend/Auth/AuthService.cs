using System.Net;
using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Auth;

public sealed class AuthService(
    IPlaatClient plaatClient,
    TokenStorageService tokenStorage,
    PersistentTokenStore persistentTokenStore,
    ITokenSessionStore tokenSessionStore,
    IProxySessionSync proxySessionSync,
    JwtAuthStateProvider authStateProvider)
{
    public async Task RestoreSession(CancellationToken cancellationToken)
    {
        if (tokenStorage.IsAuthenticated)
        {
            await proxySessionSync.Establish(tokenStorage.AccessToken!, tokenStorage.ExpiresAt!.Value);
            return;
        }

        var stored = await persistentTokenStore.Load();

        if (stored is null)
        {
            return;
        }

        tokenStorage.RefreshToken = stored.RefreshToken;
        tokenStorage.AccessToken = stored.AccessToken;
        tokenStorage.ExpiresAt = stored.ExpiresAt;

        if (tokenStorage.IsAuthenticated)
        {
            authStateProvider.NotifyUserAuthenticated();
            await proxySessionSync.Establish(tokenStorage.AccessToken!, tokenStorage.ExpiresAt!.Value);
        }
        else if (!await Refresh(cancellationToken))
        {
            await Logout();
        }
    }

    public async Task<LoginResult> Login(
        string username,
        string password,
        string? clientIp,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenResponse = await plaatClient.Login(username, password, clientIp, cancellationToken);

            if (tokenResponse is null)
            {
                return LoginResult.InvalidCredentials;
            }

            await tokenSessionStore.PersistRefreshedToken(tokenResponse, cancellationToken);
            return LoginResult.Success;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return LoginResult.TooManyAttempts;
        }
        catch
        {
            return LoginResult.InvalidCredentials;
        }
    }

    private async Task<bool> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = tokenStorage.RefreshToken;
        if (refreshToken is null)
        {
            return false;
        }

        try
        {
            var tokenResponse = await plaatClient.RefreshToken(refreshToken, cancellationToken);

            if (tokenResponse is null)
            {
                return false;
            }

            await tokenSessionStore.PersistRefreshedToken(tokenResponse, cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task Logout()
    {
        tokenStorage.Clear();
        await persistentTokenStore.Clear();
        authStateProvider.NotifyUserLoggedOut();
        await proxySessionSync.Clear();
    }
}
