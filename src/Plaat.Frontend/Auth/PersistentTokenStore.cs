using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Plaat.Contract.Models.Authorization;

namespace Plaat.Frontend.Auth;

public partial class PersistentTokenStore(
    ProtectedLocalStorage localStorage,
    ILogger<PersistentTokenStore> logger)
{
    private const string StorageKey = "auth_token";

    public async Task Save(TokenResponse tokenResponse)
    {
        try
        {
            await localStorage.SetAsync(StorageKey, tokenResponse);
        }
        catch (Exception ex)
        {
            LogSaveFailed(logger, ex);
        }
    }

    public async Task<TokenResponse?> Load()
    {
        try
        {
            var result = await localStorage.GetAsync<TokenResponse>(StorageKey);
            return result.Success ? result.Value : null;
        }
        catch (Exception ex)
        {
            LogLoadFailed(logger, ex);
            return null;
        }
    }

    public async Task Clear()
    {
        try
        {
            await localStorage.DeleteAsync(StorageKey);
        }
        catch (Exception ex)
        {
            LogClearFailed(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Error, "Could not save token response")]
    private static partial void LogSaveFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Could not load token response")]
    private static partial void LogLoadFailed(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Error, "Could not clear token response")]
    private static partial void LogClearFailed(ILogger logger, Exception ex);
}
