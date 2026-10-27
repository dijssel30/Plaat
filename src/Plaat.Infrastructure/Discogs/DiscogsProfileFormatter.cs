using System.Net;
using System.Text.RegularExpressions;

namespace Plaat.Infrastructure.Discogs;

/// <summary>
/// Cleans up Discogs profile markup (e.g. "[a=Name]", "[a123456]", "[url=..]text[/url]",
/// "[b]..[/b]") into safe, ready-to-render HTML. Named references are substituted directly;
/// bare-ID references are resolved via the Discogs API, so this should only be run once per
/// profile (at enrichment time), not on every render. The result is HTML-encoded before any of
/// our own tags are inserted, so it is safe to render as raw markup (e.g. Blazor's MarkupString).
/// </summary>
public sealed partial class DiscogsProfileFormatter(IDiscogsClient client)
{
    public async Task<string> Format(string profile, CancellationToken cancellationToken)
    {
        var text = UrlWithTextRegex().Replace(profile, m => m.Groups["text"].Value);
        text = BareUrlRegex().Replace(text, m => m.Groups["addr"].Value);
        text = await ResolveEntityReferences(text, cancellationToken);

        return ToSafeHtml(text);
    }

    private async Task<string> ResolveEntityReferences(string text, CancellationToken cancellationToken)
    {
        var matches = EntityRefRegex().Matches(text);

        if (matches.Count == 0)
        {
            return text;
        }

        var replacements = new Dictionary<string, string>();

        foreach (Match match in matches)
        {
            if (replacements.ContainsKey(match.Value))
            {
                continue;
            }

            var type = match.Groups["type"].Value[0];
            var value = match.Groups["value"].Value;

            replacements[match.Value] = int.TryParse(value, out var id)
                ? await ResolveOrFallback(type, id, match.Value, cancellationToken)
                : value;
        }

        return EntityRefRegex().Replace(text, m => replacements.GetValueOrDefault(m.Value, m.Value));
    }

    private async Task<string> ResolveOrFallback(char type, int id, string fallback, CancellationToken cancellationToken)
    {
        try
        {
            return await client.ResolveReferenceName(type, id, cancellationToken) ?? fallback;
        }
        catch (Exception)
        {
            // Best effort: one broken reference shouldn't prevent the rest of the profile from
            // being cleaned up. Leave the original markup token in place for this one.
            return fallback;
        }
    }

    /// <summary>
    /// HTML-encodes the whole string first (so any stray "&lt;"/"&amp;" etc. in the source text,
    /// or in a resolved name, can never be interpreted as markup), then swaps the remaining
    /// (unencoded, since brackets aren't HTML-encoded) bbcode-style tags for real HTML tags.
    /// </summary>
    private static string ToSafeHtml(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);

        return BbCodeTagRegex().Replace(encoded, m => m.Groups["slash"].Value switch
        {
            "" => m.Groups["tag"].Value switch { "b" => "<strong>", "i" => "<em>", "u" => "<u>", _ => m.Value },
            _ => m.Groups["tag"].Value switch { "b" => "</strong>", "i" => "</em>", "u" => "</u>", _ => m.Value }
        });
    }

    [GeneratedRegex(@"\[url=(?<href>[^\]]+)\](?<text>.*?)\[/url\]", RegexOptions.Singleline)]
    private static partial Regex UrlWithTextRegex();

    [GeneratedRegex(@"\[url\](?<addr>[^\]]+)\[/url\]", RegexOptions.Singleline)]
    private static partial Regex BareUrlRegex();

    [GeneratedRegex(@"\[(?<type>[alrm])=?(?<value>[^\]]+)\]")]
    private static partial Regex EntityRefRegex();

    [GeneratedRegex(@"\[(?<slash>/?)(?<tag>b|i|u)\]")]
    private static partial Regex BbCodeTagRegex();
}
