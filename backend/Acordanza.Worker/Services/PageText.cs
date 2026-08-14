using System.Net;
using System.Text.RegularExpressions;

namespace Acordanza.Worker.Services;

// HTML → the prose a model should read (plan 31 §4).
//
// Regex rather than a parser dependency, on plan 29 §4's supply-chain grounds and because
// the risky part of this feature is the network, not the parsing: the parsing failure mode
// is "we send slightly messier text", which the model is robust to. Deliberately crude —
// we are not rendering this, and raw HTML would be five to ten times the tokens for no
// gain.
public static partial class PageText
{
    // ~8k tokens. Enough for a blog post about an opening several times over; the tail of
    // a long page is comments and sidebars.
    public const int MaxCharacters = 30_000;

    // Below this there is nothing to read, which in practice means a JS-rendered or
    // paywalled page. Better to say so than to send the model an empty page and let it
    // hallucinate into the gap.
    public const int MinimumUsefulCharacters = 200;

    public static string Extract(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";

        var text = ScriptStyle().Replace(html, " ");
        text = Comment().Replace(text, " ");
        // A space, not the empty string: a tag is a boundary, and dropping it outright
        // would run the words on either side of a </p> together.
        text = AnyTag().Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        text = Whitespace().Replace(text, " ").Trim();

        return text.Length > MaxCharacters ? text[..MaxCharacters] : text;
    }

    [GeneratedRegex(@"<(script|style|noscript)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyle();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.Singleline)]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
