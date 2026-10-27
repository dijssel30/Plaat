using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;

namespace Plaat.Frontend.Models;

/// <summary>
/// Render-time guard for pre-formatted HTML coming from the backend. The backend already encodes
/// its text, but this keeps a stored value from ever injecting markup: every "&lt;" that is not
/// part of an allowed formatting tag is escaped.
/// </summary>
public static partial class SafeMarkup
{
    public static MarkupString Discogs(string html)
    {
        return new MarkupString(DisallowedTagStartRegex().Replace(html, "&lt;"));
    }

    [GeneratedRegex("<(?!/?(?:strong|em|u)>)")]
    private static partial Regex DisallowedTagStartRegex();
}
