namespace Plaat.Contract.Models.Authorization;

/// <summary>
/// In-memory holder for the current access/refresh token pair and expiry.
/// </summary>
public sealed class TokenStorageService
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }

    public bool IsAuthenticated
    {
        get
        {
            return AccessToken is not null
                   && ExpiresAt.HasValue
                   && ExpiresAt.Value > DateTime.UtcNow;
        }
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        ExpiresAt = null;
    }
}
