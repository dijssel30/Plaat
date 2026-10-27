using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Plaat.Frontend;

/// <summary>
/// Maps an opaque session id (held by the browser only in an HttpOnly cookie) to the
/// real API access token, so the token itself never has to leave the server or appear
/// in a URL/DOM that the browser exposes to the user or to JavaScript.
/// </summary>
public sealed class ProxySessionStore
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new();

    public string Establish(string? existingSessionId, string accessToken, DateTime expiresAt)
    {
        var sessionId = existingSessionId is not null && _sessions.ContainsKey(existingSessionId)
            ? existingSessionId
            : CreateSessionId();

        _sessions[sessionId] = new Session(accessToken, expiresAt);

        return sessionId;
    }

    public string? GetAccessToken(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return null;
        }

        if (session.ExpiresAt <= DateTime.UtcNow)
        {
            _sessions.TryRemove(sessionId, out _);

            return null;
        }

        return session.AccessToken;
    }

    public void Remove(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    private static string CreateSessionId()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private readonly record struct Session(string AccessToken, DateTime ExpiresAt);
}
