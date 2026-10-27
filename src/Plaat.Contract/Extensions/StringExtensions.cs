using System.Globalization;
using System.Text;

namespace Plaat.Contract.Extensions;

public static class StringExtensions
{
    public static string NormalizeForSearch(this string input)
    {
        var normalized = input.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);

            if (category is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = c switch
            {
                'ð' => 'd',
                'Ð' => 'd',
                'þ' => 't',
                'Þ' => 't',
                'ø' => 'o',
                'Ø' => 'o',
                'ß' => 's',
                _ => char.ToLowerInvariant(c)
            };

            builder.Append(mapped);
        }

        return builder.ToString()
            .Normalize(NormalizationForm.FormC);
    }
}
