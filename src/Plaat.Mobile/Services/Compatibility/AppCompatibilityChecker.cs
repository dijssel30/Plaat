using Plaat.Contract.Models;

namespace Plaat.Mobile.Services.Compatibility;

/// <summary>
/// Pure version-range comparison backing the one-time startup compatibility check (see
/// <see cref="AppShell"/>'s startup routine) - kept free of any MAUI dependency so it's
/// unit-testable without the Android/MAUI runtime.
/// </summary>
public static class AppCompatibilityChecker
{
    /// <summary>
    /// Returns false only when <paramref name="currentAppVersion"/> parses as a <see cref="Version"/>
    /// and falls strictly outside the given range. An unparseable current version, or an unparseable/
    /// absent bound, is treated as compatible - this check exists to warn, not to gate startup.
    /// </summary>
    public static bool IsCompatible(string currentAppVersion, AppCompatibilityDto compatibility)
    {
        if (!Version.TryParse(currentAppVersion, out var current))
        {
            return true;
        }

        if (Version.TryParse(compatibility.MinimumAppVersion, out var minimum) && current < minimum)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(compatibility.MaximumAppVersion)
            && Version.TryParse(compatibility.MaximumAppVersion, out var maximum)
            && current > maximum)
        {
            return false;
        }

        return true;
    }
}
