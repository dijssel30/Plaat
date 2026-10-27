namespace Plaat.Biz.Options;

/// <summary>
/// Options for jwt token authentication to the backend.
/// </summary>
public sealed class JwtOptions
{
    /// <summary>
    /// Key to use for signing the bearer tokens. Make sure it is hard to guess and longer than 14 characters.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>
    /// Issuer name of the bearer tokens.
    /// </summary>
    public const string Issuer = "Plaat.Api";

    /// <summary>
    /// Audience name for the bearer tokens.
    /// </summary>
    public const string Audience = "Plaat.Api";
}
