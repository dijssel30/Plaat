using Microsoft.JSInterop;

namespace Plaat.Frontend.Auth;

/// <summary>
/// Syncs the current auth session to the browser side (JS interop) so a reverse proxy or
/// service worker can see it.
/// </summary>
public interface IProxySessionSync
{
    /// <summary>
    /// Pushes the current access token and its expiry to the browser side.
    /// </summary>
    Task Establish(string accessToken, DateTime expiresAt);

    /// <summary>
    /// Clears any session state held on the browser side.
    /// </summary>
    Task Clear();
}

public partial class ProxySessionSync(IJSRuntime jsRuntime, ILogger<ProxySessionSync> logger) : IProxySessionSync
{
    public Task Establish(string accessToken, DateTime expiresAt)
    {
        return Invoke("plaatSession.establish", accessToken, expiresAt);
    }

    public Task Clear()
    {
        return Invoke("plaatSession.clear");
    }

    private async Task Invoke(string identifier, params object?[] args)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync(identifier, args);
        }
        catch (JSDisconnectedException ex)
        {
            LogCircuitDisconnected(logger, ex);
        }
        catch (InvalidOperationException ex)
        {
            LogJsInteropUnavailable(logger, ex);
        }
    }

    [LoggerMessage(LogLevel.Warning, "Could not sync proxy session; circuit disconnected")]
    private static partial void LogCircuitDisconnected(ILogger logger, Exception ex);

    [LoggerMessage(LogLevel.Warning, "Could not sync proxy session; JS interop not yet available")]
    private static partial void LogJsInteropUnavailable(ILogger logger, Exception ex);
}
