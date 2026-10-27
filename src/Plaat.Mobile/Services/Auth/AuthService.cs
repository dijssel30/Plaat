using Plaat.Contract.Client;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Mobile.Services.Auth;

/// <summary>
/// Port of Plaat.Frontend/Auth/AuthService.cs, minus the Blazor-only cookie/proxy-session/
/// JwtAuthStateProvider machinery (none of that applies to a native app - there's no browser
/// pre-render or SignalR circuit to keep in sync).
/// </summary>
public sealed class AuthService(
    IPlaatClient plaatClient,
    TokenStorageService tokenStorage,
    AppSecureTokenSessionStore tokenSessionStore)
{
    /// <summary>
    /// Cached from <c>GetCurrentUser()</c> on login/session restore rather than re-checked per
    /// track - read by <see cref="Player.PlaybackEngine"/> to decide whether to apply loudness
    /// attenuation. Defaults to enabled so playback behaves normally before the first fetch settles.
    /// </summary>
    public bool LoudnessNormalizationEnabled { get; private set; } = true;

    /// <summary>
    /// Called once at app startup. Restores the session from the stored refresh token if the app
    /// was killed and relaunched (there's never an in-memory access token to fall back on the way
    /// Blazor Server's per-circuit state might have one).
    /// </summary>
    public async Task RestoreSession(CancellationToken cancellationToken)
    {
        if (tokenStorage.IsAuthenticated)
        {
            await RefreshCurrentUserSettings(cancellationToken);
            return;
        }

        var refreshToken = await AppSecureTokenSessionStore.LoadStoredRefreshToken();

        if (refreshToken is null)
        {
            return;
        }

        tokenStorage.RefreshToken = refreshToken;

        if (!await Refresh(cancellationToken))
        {
            await Logout();
            return;
        }

        await RefreshCurrentUserSettings(cancellationToken);
    }

    public async Task<bool> Login(string username, string password, CancellationToken cancellationToken)
    {
        try
        {
            // No clientIp: unlike Plaat.Frontend (which calls the API server-side on the browser's
            // behalf, so the API would otherwise only see the frontend server's IP), a native app's
            // HttpClient talks to the API directly and the API sees the phone's real IP already.
            var tokenResponse = await plaatClient.Login(username, password, clientIp: null, cancellationToken);

            if (tokenResponse is null)
            {
                return false;
            }

            await tokenSessionStore.PersistRefreshedToken(tokenResponse, cancellationToken);
            await RefreshCurrentUserSettings(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RefreshCurrentUserSettings(CancellationToken cancellationToken)
    {
        try
        {
            var currentUser = await plaatClient.GetCurrentUser(cancellationToken);

            if (currentUser is not null)
            {
                LoudnessNormalizationEnabled = currentUser.LoudnessNormalizationEnabled;
            }
        }
        catch
        {
            // keep whatever value we already have (default: enabled)
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

    public Task Logout()
    {
        tokenStorage.Clear();
        AppSecureTokenSessionStore.ClearStoredRefreshToken();
        return Task.CompletedTask;
    }
}
