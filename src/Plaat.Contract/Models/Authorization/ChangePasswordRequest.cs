using System.ComponentModel.DataAnnotations;

namespace Plaat.Contract.Models.Authorization;

public sealed record ChangePasswordRequest(
    [Required(AllowEmptyStrings = false)]
    string CurrentPassword,
    [Required(AllowEmptyStrings = false)]
    [MinLength(8)]
    string NewPassword);
