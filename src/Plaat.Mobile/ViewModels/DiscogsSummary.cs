using System.Net;
using System.Text.RegularExpressions;

namespace Plaat.Mobile.ViewModels;

/// <summary>
/// Turns the API's ready-to-render Discogs bio HTML into the plain text a MAUI Label can show.
/// No MAUI dependency, so it can be unit tested (see Plaat.Mobile.Tests.csproj).
/// </summary>
public static partial class DiscogsSummary
{
    /// <summary>
    /// Strips the tags, then undoes the HTML encoding the API applied to the profile text. Both
    /// steps matter, and in this order: without the decode an "&amp;" in the bio reaches the label
    /// as a literal "&amp;amp;", and decoding first would turn an encoded angle bracket from the
    /// original profile text into something the tag-stripping regex would then eat.
    /// </summary>
    public static string? ToPlainText(string? html)
    {
        return html is null
            ? null
            : WebUtility.HtmlDecode(HtmlTagRegex().Replace(html, " ")).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();
}
