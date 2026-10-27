namespace Plaat.Contract.Models;

/// <summary>
/// The range of Plaat.Mobile app versions this backend is compatible with, compared against
/// <c>AppInfo.Current.VersionString</c> on app startup so an outdated (or too-new) app build can
/// warn the user instead of silently misbehaving against an incompatible API.
/// </summary>
public sealed record AppCompatibilityDto(string MinimumAppVersion, string? MaximumAppVersion);
