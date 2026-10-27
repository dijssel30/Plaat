using System.ComponentModel.DataAnnotations;

namespace Plaat.Contract.Models.Authorization;

/// <summary>
/// Request to create a new user account.
/// </summary>
public sealed class CreateUserRequest
{
    [Required(AllowEmptyStrings = false)]
    [MinLength(3)]
    public required string Username { get; init; }

    [Required(AllowEmptyStrings = false)]
    [MinLength(8)]
    public required string Password { get; init; }

    public bool IsAdmin { get; init; }
}
