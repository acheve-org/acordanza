using System.Text.RegularExpressions;
using Acordanza.Core.Models;

namespace Acordanza.Worker.Services;

// The French grade and/or aid grade a source printed, plus the obligatory grade printed
// next to it when there was one. ObligatoryGrade is null far more often than not — it is a
// multi-pitch convention, and most single-pitch topos and blog posts print one number.
// AidGrade is likewise null far more often than not — «6c/A1» (both) and «A2» (aid only)
// are the exceptions, not the rule.
public record ParsedGrade(string? Grade, string? AidGrade, string? ObligatoryGrade);

// Both extraction prompts tell the model to copy the grade **exactly as printed** and
// never to convert between scales, which is the right instruction (a converted grade is a
// guess wearing the costume of data) and has a consequence: what arrives is whatever the
// source prints in the place a grade goes, and on a multi-pitch topo that is routinely
// three facts in one string —
//
//     MD+ 6c (6a+ obl)
//      │   │    └── the obligatory grade: what you must actually free → ObligatoryGrade
//      │   └─────── the hardest move, which is what this log calls Grade
//      └─────────── the alpine difficulty grade, which this log has no field for
//
// GradeValidator.IsValid sees one unparseable string and the whole import is discarded,
// which is how «Núria MD+ 6c (6a+ obl)» produced RouteImportOutcomes.UnreadableGrade off a
// topo a human reads at a glance. This is the pull-apart, and it is deliberately narrow:
//
//   - Everything outside the parentheses is read **strictly**. A token has to be a French
//     grade or one of the decorations named below; a token this does not recognise fails
//     the whole parse, so a British «E2 5b» or a UIAA «V+» stays the discard it must be
//     rather than being mined for a substring that looks French. Two different grades out
//     there (a range, a pitch list) is likewise a discard: a confidently wrong grade in a
//     catalogue whose whole point is that people entered it is worse than a stated failure
//     the submitter can act on — the same call TopoImageReader.Accept makes on
//     multipleRoutes.
//   - Everything inside the parentheses is read **leniently**, because parentheses carry
//     prose as often as data («(6a+ obl, muy sostenido)»), and nothing in there can become
//     the route's grade anyway. An obligatory grade is only taken when the source labelled
//     it one: a bare «6c (6a+)» leaves ObligatoryGrade null rather than guessing at a
//     convention that is common but not universal.
//
// Worker-side rather than in Core next to GradeValidator, per plan 22's rule: this is the
// machine-reading path's problem, and the human-entry path in RoutesController must go on
// rejecting «MD+ 6c (6a+ obl)» in the grade box.
public static partial class PrintedGrade
{
    [GeneratedRegex(@"^[1-9][a-c]\+?$")]
    private static partial Regex FrenchGrade { get; }

    // The artificial (aid) grade scale, lowercase form — Parse works on text already
    // GradeValidator.Normalize'd (trim + lowercase), unlike AidGradeValidator's own
    // uppercase canonical form.
    [GeneratedRegex(@"^[ac][0-5]\+?$")]
    private static partial Regex AidGradeToken { get; }

    // Same grade with the marker glued on — «6a+obl.», which is how it comes off a topo
    // set in a narrow column.
    [GeneratedRegex(@"^([1-9][a-c]\+?)[.\-]?(ob|obl|oblig|obligado|obligatorio)\.?$")]
    private static partial Regex GradeWithMarker { get; }

    [GeneratedRegex(@"^(ob|obl|oblig|obligado|obligatorio)\.?$")]
    private static partial Regex ObligatoryMarker { get; }

    // The alpine/global difficulty grade a multi-pitch route carries alongside its free
    // grade: F, PD, AD, D, MD, TD, ED, ABO, EX, optionally numbered and signed («ED2»,
    // «MD+», «TD-»). Recognised only to be thrown away — this log grades routes on the
    // French scale and has nowhere to put it.
    [GeneratedRegex(@"^(f|pd|ad|d|md|td|ed|abo|ex)[0-9]?(\+|-|\+/-)?$")]
    private static partial Regex AlpineDifficulty { get; }

    // Seriousness/equipment ratings, the other thing Spanish topos print in the grade line
    // («SR2+», «R», «X»). Thrown away for the same reason.
    [GeneratedRegex(@"^(s|r|sr|rs|x)[0-9]?[+-]?$")]
    private static partial Regex Seriousness { get; }

    [GeneratedRegex(@"\(([^)]*)\)")]
    private static partial Regex Parenthetical { get; }

    // Not '/': «6a/6b» and «6c/A1» stay one token, which this does not recognise, which
    // makes them a discard. That is the intended answer — neither says one grade.
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ',', ';', '(', ')'];

    /// The French grade this string is really stating, or null when it doesn't state
    /// exactly one. The returned Grade is already GradeValidator.Normalize'd, so callers
    /// write it straight through.
    public static ParsedGrade? Parse(string? printed)
    {
        var text = GradeValidator.Normalize(printed);
        if (text.Length == 0) return null;

        // Overwhelmingly the common case: the source printed one clean French grade, and
        // this costs one regex before any of the machinery below.
        if (GradeValidator.IsValid(text)) return new ParsedGrade(text, null, null);

        // The other single-token case: an aid-only route, printed as just «A2».
        if (AidGradeToken.IsMatch(text)) return new ParsedGrade(null, AidGradeValidator.Normalize(text), null);

        string? parenthesised = null;
        foreach (Match match in Parenthetical.Matches(text))
        {
            parenthesised ??= ObligatoryIn(Tokenize(match.Groups[1].Value));
        }

        // Balanced groups are gone; a stray unclosed '(' is handled by Separators, so
        // «6c (6a+ obl» reads the same as the closed form rather than failing on "(6a+".
        var (grade, aidGrade, alongside) = MainGradeIn(Tokenize(Parenthetical.Replace(text, " ")));

        return grade is null && aidGrade is null
            ? null
            : new ParsedGrade(grade, aidGrade is null ? null : AidGradeValidator.Normalize(aidGrade), parenthesised ?? alongside);
    }

    // A token stating both scales at once — «6c/a1» or «a1/6c» — the '/' shape §11 of plan
    // 34 gives exactly one meaning to: one free grade and one aid grade, in either order.
    // Anything else with a '/' (a range like «6a/6b») fails both checks and falls through
    // to being an ordinary unrecognised token.
    private static (string Grade, string AidGrade)? SplitCombined(string token)
    {
        var parts = token.Split('/');
        if (parts.Length != 2) return null;

        if (FrenchGrade.IsMatch(parts[0]) && AidGradeToken.IsMatch(parts[1])) return (parts[0], parts[1]);
        if (AidGradeToken.IsMatch(parts[0]) && FrenchGrade.IsMatch(parts[1])) return (parts[1], parts[0]);
        return null;
    }

    // Strict: exactly one French grade, at most one aid grade, and every other token
    // accounted for.
    private static (string? Grade, string? AidGrade, string? Obligatory) MainGradeIn(List<string> tokens)
    {
        // Which grade each «obl» is claiming. The marker need not be bracketed — «6c 6a+
        // obl» prints the same two facts as «6c (6a+ obl)» — and it binds *forwards* when
        // a grade follows it, backwards otherwise. That asymmetry is the only reading that
        // survives «6c obl. 6a+», where treating both neighbours alike would take the
        // route's own grade for the obligatory one and discard the line as gradeless.
        var claimed = new bool[tokens.Count];
        for (var i = 0; i < tokens.Count; i++)
        {
            if (GradeWithMarker.IsMatch(tokens[i])) claimed[i] = true;
            if (!ObligatoryMarker.IsMatch(tokens[i])) continue;

            if (i + 1 < tokens.Count && GradeIn(tokens[i + 1]) is not null) claimed[i + 1] = true;
            else if (i > 0 && GradeIn(tokens[i - 1]) is not null) claimed[i - 1] = true;
        }

        string? grade = null;
        string? aidGrade = null;
        string? obligatory = null;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (AlpineDifficulty.IsMatch(token) || Seriousness.IsMatch(token) || ObligatoryMarker.IsMatch(token))
            {
                continue;
            }

            if (SplitCombined(token) is { } combo)
            {
                if (grade is not null && grade != combo.Grade) return (null, null, null);
                if (aidGrade is not null && aidGrade != combo.AidGrade) return (null, null, null);
                grade = combo.Grade;
                aidGrade = combo.AidGrade;
                continue;
            }

            if (AidGradeToken.IsMatch(token))
            {
                if (aidGrade is not null && aidGrade != token) return (null, null, null);
                aidGrade = token;
                continue;
            }

            if (GradeIn(token) is not { } candidate) return (null, null, null);

            if (claimed[i])
            {
                obligatory ??= candidate;
                continue;
            }

            if (grade is not null && grade != candidate) return (null, null, null);
            grade = candidate;
        }

        return (grade, aidGrade, obligatory);
    }

    // Lenient: anything unrecognised in here is prose, not a failure. Requires the label,
    // and requires the group to name only one grade — «(6a+ o 6b obl)» is not an answer.
    private static string? ObligatoryIn(List<string> tokens)
    {
        if (!tokens.Any(t => ObligatoryMarker.IsMatch(t) || GradeWithMarker.IsMatch(t))) return null;

        var grades = tokens.Select(GradeIn).OfType<string>().Distinct().ToList();
        return grades.Count == 1 ? grades[0] : null;
    }

    private static string? GradeIn(string token)
    {
        if (FrenchGrade.IsMatch(token)) return token;

        var withMarker = GradeWithMarker.Match(token);
        return withMarker.Success ? withMarker.Groups[1].Value : null;
    }

    private static List<string> Tokenize(string text) =>
        [.. text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
}
