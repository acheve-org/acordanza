using System.Net;
using System.Text.RegularExpressions;
using Acordanza.Core.Services;

namespace Acordanza.Worker.Services;

// HTML → an ordered, capped list of image URLs worth vision-checking (plan 31 §4).
//
// A post about an opening usually carries the topo as an image, which is the whole reason
// this exists. Ordering is the cheap half of the work: the first hit wins downstream, so
// putting the lead image first is what keeps the typical run at one or two vision calls
// instead of eight.
//
// Static and no-I/O, like PageText, and for the same reason: the failure mode here is "we
// miss an image", not "we do something unsafe" — the safety is entirely in PageFetcher,
// which is what actually opens these.
public static partial class PageImages
{
    // Eight Haiku calls at ~$0.002 is the worst case, and it stops at the first hit.
    public const int MaxCandidates = 8;

    // A tracking pixel and a share button are not topos, and neither is anything a
    // theme ships. Matched against the path, so a query string can't smuggle one past.
    private static readonly string[] FurniturePathFragments =
    [
        "avatar", "gravatar", "icon", "logo", "emoji", "badge", "sprite", "spinner"
    ];

    public static List<Uri> Extract(string html, Uri baseUri)
    {
        if (string.IsNullOrEmpty(html)) return [];

        // <base href> wins over the response URI when the page declares one — that is what
        // the browser would resolve against, and getting it wrong on a blogspot template
        // means every candidate 404s.
        var resolveAgainst = BaseHref(html) is { } declared &&
            Uri.TryCreate(baseUri, declared, out var rebased) ? rebased : baseUri;

        var ordered = new List<string>();

        // The lead image first: on a post about an opening it is the topo more often than
        // not, which is worth one ordering rule and no extra requests.
        foreach (Match match in MetaImage().Matches(html))
        {
            ordered.Add(match.Groups["content"].Value);
        }

        foreach (Match match in ImgTag().Matches(html))
        {
            var tag = match.Value;
            if (IsTooSmall(tag)) continue;

            // src, then the largest srcset entry, then the lazy-load attributes blogspot
            // and wordpress use heavily — those are exactly the long-tail hosts plan 29 §3
            // went out of its way to keep in scope, and on them `src` is frequently a
            // placeholder while the real image hides in data-src.
            foreach (var candidate in new[]
                     {
                         Attribute(tag, "src"),
                         LargestSrcSetEntry(Attribute(tag, "srcset")),
                         Attribute(tag, "data-src"),
                         Attribute(tag, "data-lazy-src"),
                         Attribute(tag, "data-original")
                     })
            {
                if (!string.IsNullOrWhiteSpace(candidate)) ordered.Add(candidate!);
            }
        }

        var seen = new HashSet<string>();
        var result = new List<Uri>();
        foreach (var raw in ordered)
        {
            var decoded = WebUtility.HtmlDecode(raw).Trim();
            if (decoded.Length == 0) continue;
            // data: URIs are dropped rather than decoded: an inline image on a climbing
            // post is furniture, and accepting them would mean a page could hand us
            // megabytes with no fetch to cap.
            if (decoded.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;

            if (!Uri.TryCreate(resolveAgainst, decoded, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            if (IsFurniture(uri)) continue;

            // Deduped through the same normaliser enrichment grounds its links with, so a
            // page linking the same photo with and without a tracking parameter costs one
            // vision call rather than two.
            if (!seen.Add(UrlCanonicalizer.Canonical(uri.ToString()))) continue;

            result.Add(uri);
            if (result.Count == MaxCandidates) break;
        }

        return result;
    }

    private static bool IsFurniture(Uri uri)
    {
        var path = uri.AbsolutePath.ToLowerInvariant();
        return FurniturePathFragments.Any(fragment => path.Contains(fragment));
    }

    // Any declared width or height under 100 px. Only the attribute is read — CSS is not
    // parsed, and a lazy-loaded image often declares nothing at all, which is treated as
    // "big enough" rather than as a reason to drop it.
    private static bool IsTooSmall(string tag)
    {
        foreach (var name in new[] { "width", "height" })
        {
            var value = Attribute(tag, name);
            if (int.TryParse(value, out var pixels) && pixels < 100) return true;
        }
        return false;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, name + @"\s*=\s*(?:""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^\s>]+))",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["v"].Value : null;
    }

    // `srcset` is "url 320w, url 1024w" (or "url 2x"): the largest descriptor is the one
    // worth reading a topo off, and the smallest is usually a thumbnail of it.
    private static string? LargestSrcSetEntry(string? srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset)) return null;

        string? best = null;
        double bestWeight = -1;
        foreach (var entry in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = entry.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var weight = 1d;
            if (parts.Length > 1)
            {
                var descriptor = parts[1];
                if (double.TryParse(descriptor.TrimEnd('w', 'x', 'W', 'X'),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var parsed))
                {
                    weight = parsed;
                }
            }

            if (weight > bestWeight)
            {
                bestWeight = weight;
                best = parts[0];
            }
        }
        return best;
    }

    private static string? BaseHref(string html)
    {
        var match = BaseTag().Match(html);
        return match.Success ? WebUtility.HtmlDecode(match.Groups["href"].Value).Trim() : null;
    }

    [GeneratedRegex("""<meta[^>]*(?:property|name)\s*=\s*["'](?:og:image(?::url)?|twitter:image(?::src)?)["'][^>]*content\s*=\s*["'](?<content>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex MetaImage();

    [GeneratedRegex(@"<img\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImgTag();

    [GeneratedRegex("""<base[^>]*href\s*=\s*["'](?<href>[^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex BaseTag();
}
