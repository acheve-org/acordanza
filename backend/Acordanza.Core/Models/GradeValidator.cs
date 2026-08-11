using System.Text.RegularExpressions;

namespace Acordanza.Core.Models;

// The French sport-grade scale, and the only grading system the app accepts (issue #152):
// a number 1–9, a letter a–c, and an optional '+'. Mirrored client-side by
// design-system/src/components/GradePill/gradeBand.ts — keep the two in step by hand.
public static partial class GradeValidator
{
    [GeneratedRegex(@"^[1-9][a-c]\+?$")]
    private static partial Regex Pattern { get; }

    /// Trim + lowercase. Grade is an exact-match filter key and a Distinct() list, so the
    /// stored form has to be canonical — "6A " and "6a" are the same grade, not two.
    public static string Normalize(string? grade) => (grade ?? "").Trim().ToLowerInvariant();

    public static bool IsValid(string? grade) => Pattern.IsMatch(Normalize(grade));
}
