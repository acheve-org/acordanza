namespace Acordanza.Core.Services;

// One normaliser for "are these two URLs the same page", hoisted out of
// ClaudeRouteEnrichmentProvider (plan 31 §4) once three callers needed it: enrichment's
// grounding check, the image sweep's dedupe, and the API canonicalising a submitted URL
// so a re-paste differing only by a tracking parameter short-circuits against the row
// that already exists.
//
// In Core rather than in the worker, unlike the rest of plan 31's parsing helpers: the
// API is one of those three callers and never references Acordanza.Worker.
public static class UrlCanonicalizer
{
    private static readonly HashSet<string> TrackingParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "gbraid", "wbraid", "msclkid", "mc_cid", "mc_eid", "igshid"
    };

    // Normalises before comparing, because otherwise a link differing from its search
    // result only by a `?utm_source=` is dropped as ungrounded — trading one failure mode
    // for another. Redirect chains are deliberately *not* followed here: the reachability
    // check in RouteEnrichmentService already resolves them, and doing it twice doubles the
    // request count for no extra signal.
    public static string Canonical(string url)
    {
        var trimmed = (url ?? "").Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return trimmed.ToLowerInvariant();
        }

        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/')) path = path.TrimEnd('/');
        if (path == "/") path = "";

        var kept = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(pair =>
            {
                var key = pair.Split('=', 2)[0];
                return !key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) &&
                    !TrackingParameters.Contains(key);
            })
            .ToList();
        var query = kept.Count > 0 ? "?" + string.Join("&", kept) : "";

        var authority = uri.Host.ToLowerInvariant() + (uri.IsDefaultPort ? "" : $":{uri.Port}");
        return $"{uri.Scheme.ToLowerInvariant()}://{authority}{path}{query}";
    }
}
