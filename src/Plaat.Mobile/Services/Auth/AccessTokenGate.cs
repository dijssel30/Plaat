using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Mobile.Services.Auth;

/// <summary>
/// Keeps <see cref="TokenStorageService"/>'s access token fresh for the loopback proxy
/// (<see cref="Streaming.LoopbackProxyService"/>), which needs a valid bearer token per forwarded
/// request but isn't itself an <see cref="IPlaatClient"/> call going through
/// <see cref="PlaatClient"/>'s own proactive-refresh logic.
///
/// Deliberately does not duplicate the refresh HTTP call or token persistence - both go through the
/// already-public <see cref="IPlaatClient.RefreshToken"/> and <see cref="ITokenSessionStore.PersistRefreshedToken"/>,
/// same as PlaatClient.EnsureFreshAccessToken (Plaat.Contract/Client/PlaatClient.cs) uses internally.
/// Only the "is it time yet, don't race concurrent callers" bookkeeping is duplicated here, since
/// that logic is private on PlaatClient and not exposed.
/// </summary>
public sealed class AccessTokenGate(
    IPlaatClient plaatClient,
    TokenStorageService tokenStorage,
    ITokenSessionStore tokenSessionStore)
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    // Same reasoning as PlaatClient.RefreshCallTimeout (Plaat.Contract/Client/PlaatClient.cs):
    // a caller's own cancellation token must never cut off the refresh call itself, or the
    // server can commit a rotation the client never receives - leaving it holding a refresh
    // token the server has already revoked, which reads as theft and revokes every session for
    // the account on the next refresh from anywhere (this app or the web frontend).
    private static readonly TimeSpan RefreshCallTimeout = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public async Task<string?> GetValidAccessToken(CancellationToken cancellationToken)
    {
        if (!NeedsRefresh())
        {
            return tokenStorage.AccessToken;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            if (!NeedsRefresh())
            {
                return tokenStorage.AccessToken;
            }

            if (tokenStorage.RefreshToken is null)
            {
                return tokenStorage.AccessToken;
            }

            using var refreshCts = new CancellationTokenSource(RefreshCallTimeout);

            var tokenResponse = await plaatClient.RefreshToken(tokenStorage.RefreshToken, refreshCts.Token);

            if (tokenResponse is not null)
            {
                await tokenSessionStore.PersistRefreshedToken(tokenResponse, refreshCts.Token);
            }
        }
        catch
        {
            // best-effort: proceed with whatever token we have, same as PlaatClient does.
        }
        finally
        {
            _refreshLock.Release();
        }

        return tokenStorage.AccessToken;

        bool NeedsRefresh()
        {
            if (tokenStorage.AccessToken is null || tokenStorage.RefreshToken is null)
            {
                return false;
            }

            return tokenStorage.ExpiresAt is null || tokenStorage.ExpiresAt <= DateTime.UtcNow.Add(RefreshMargin);
        }
    }
}
