namespace Plaat.Db.Models;

public class RefreshToken
{
    public int Id { get; init; }
    public required string TokenHash { get; init; }
    public int UserId { get; init; }
    public User User { get; init; } = null!;

    /// <summary>
    /// Identifies the chain of tokens descending from a single login. Rotation carries the family
    /// forward; a new login starts a new one. Reuse detection revokes a family, never the whole
    /// account, so a session on one device cannot sign the user out on another.
    /// </summary>
    public required string FamilyId { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public bool IsRevoked { get; set; }

    /// <summary>
    /// When this token was revoked, whether by rotation, an explicit revoke or reuse detection.
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// <see cref="TokenHash"/> of the token issued in this one's place when it was rotated
    /// normally. Null for a token revoked any other way, which is what distinguishes "superseded"
    /// from "killed" when a client presents a revoked token - see
    /// UserRepository.RotateRefreshToken.
    /// <para>
    /// The successor's hash rather than its Id, so a rotation is a single insert-and-update that
    /// commits at once. Pointing at the Id would mean saving the successor first to have one to
    /// point at, leaving a window in which this token looks revoked-without-a-successor - which is
    /// exactly the state a concurrent replay would misread as theft.
    /// </para>
    /// </summary>
    public string? ReplacedByTokenHash { get; set; }
}
