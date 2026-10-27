namespace Plaat.Contract.Models.Authorization;

public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTime ExpiresAt);
