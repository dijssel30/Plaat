using System.ComponentModel.DataAnnotations;

namespace Plaat.Contract.Models.Authorization;

public sealed record SetPasswordRequest(
    [Required(AllowEmptyStrings = false)]
    [MinLength(8)]
    string NewPassword);
