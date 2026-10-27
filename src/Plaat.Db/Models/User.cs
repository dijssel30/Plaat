namespace Plaat.Db.Models;

public class User
{
    public int Id { get; init; }
    public required string Username { get; init; }
    public required string PasswordHash { get; set; }
    public bool IsAdmin { get; set; }
    public bool LoudnessNormalizationEnabled { get; set; } = true;
    public ICollection<RefreshToken> RefreshTokens { get; init; } = [];
}
