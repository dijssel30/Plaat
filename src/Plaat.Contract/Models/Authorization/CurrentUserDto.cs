namespace Plaat.Contract.Models.Authorization;

public sealed record CurrentUserDto(string Username, bool IsAdmin, bool LoudnessNormalizationEnabled);
