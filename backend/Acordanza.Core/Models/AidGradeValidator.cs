using System.Text.RegularExpressions;

namespace Acordanza.Core.Models;

// The artificial (aid) grade scale: A0–A5 for aid that places gear which can damage the
// rock, C0–C5 for clean aid, each optionally '+'. Independent of GradeValidator's French
// free scale — a route can carry one, both («6c/A1») or neither. Mirrored client-side by
// design-system/src/components/GradePill/aidGrade.ts — keep the two in step by hand.
public static partial class AidGradeValidator
{
    [GeneratedRegex(@"^[AC][0-5]\+?$")]
    private static partial Regex Pattern { get; }

    /// Trim + UPPERCASE — note the asymmetry with GradeValidator.Normalize, which
    /// lowercases. Both are canonicalising to how climbers actually write the scale: «6a»
    /// is lowercase, «A1» is not. Same reason either way: this is an exact-match filter
    /// key and a Distinct() list, so «a1» and «A1» must not be two grades.
    public static string Normalize(string? grade) => (grade ?? "").Trim().ToUpperInvariant();

    public static bool IsValid(string? grade) => Pattern.IsMatch(Normalize(grade));
}
