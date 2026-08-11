namespace Acordanza.Core.Services;

// Route link validation: trim/drop-empty, cap at 10, max length, http(s)-only
// (the stored-XSS guard — a javascript: URL must never round-trip through Links),
// dedupe. Shared by RoutesController (human-entered links) and the enrichment
// worker (model-found links, RouteEnrichmentService) so the allowlist is never
// re-derived in two places.
public static class LinkRules
{
    public const int MaxLinks = 10;
    public const int MaxLinkLength = 2048;

    public static (List<string>? Urls, string? Error) Resolve(IEnumerable<string>? links)
    {
        var trimmed = (links ?? []).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        if (trimmed.Count > MaxLinks)
        {
            return (null, "no puedes añadir más de 10 enlaces");
        }

        if (trimmed.Any(l => l.Length > MaxLinkLength))
        {
            return (null, "algún enlace es demasiado largo");
        }

        var invalid = trimmed.Any(l =>
            !Uri.TryCreate(l, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps));
        if (invalid)
        {
            return (null, "algún enlace no es válido: debe empezar por http:// o https://");
        }

        return (trimmed.Distinct().ToList(), null);
    }
}
