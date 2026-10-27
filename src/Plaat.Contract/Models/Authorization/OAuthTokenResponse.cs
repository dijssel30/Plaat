using System.Text.Json.Serialization;

namespace Plaat.Contract.Models.Authorization;

/// <summary>
/// Shape mandated by the OAuth2 password-grant response, which is what Swagger UI's
/// "Authorize" dialog expects back from the token URL.
/// </summary>
public sealed record OAuthTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);
