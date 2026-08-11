using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Acordanza.Core.Services;

// Turns arbitrary user-entered text (accents, spaces, punctuation, any Unicode)
// into a lowercase ASCII token safe to use as an Azure Blob Storage path
// segment. Never returns an empty string.
public static partial class Slugify
{
    public static string ToSlug(string input, string fallback = "sector")
    {
        var decomposed = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        var lowered = sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var slug = NonAlphanumericRun().Replace(lowered, "-").Trim('-');

        return string.IsNullOrEmpty(slug) ? fallback : slug;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRun();
}
