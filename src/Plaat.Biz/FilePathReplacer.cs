using System.Diagnostics.CodeAnalysis;
using Plaat.Biz.Options;

namespace Plaat.Biz;

public sealed class FilePathReplacer(PlaatOptions plaatOptions)
{
    [return: NotNullIfNotNull(nameof(path))]
    public string? GetTargetPath(string? path)
    {
        if (path is null)
        {
            return null;
        }

        if (plaatOptions.ReplaceOptions is not { } options)
        {
            return path.Replace(@"\", "/");
        }

        var replacePath = options
            .ReplacePaths
            .FirstOrDefault(p => path.StartsWith(p.From, StringComparison.OrdinalIgnoreCase));

        return replacePath is null
            ? path.Replace(@"\", "/")
            : (replacePath.To + path[replacePath.From.Length..]).Replace(@"\", "/");
    }
}
