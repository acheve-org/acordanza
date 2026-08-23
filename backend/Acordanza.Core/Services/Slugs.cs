namespace Acordanza.Core.Services;

// The one implementation of "slugify, then append -2, -3, ... until free" shared by
// every entity that derives a URL/storage-safe slug from a user-entered name (routes,
// areas, first ascensionists). Callers supply their own uniqueness predicate (scoped
// however makes sense for them — per-area, per-parent, flat, ...) and, where relevant,
// their own Slugify fallback token.
public static class Slugs
{
    public static async Task<string> GenerateUniqueAsync(
        string baseName, Func<string, Task<bool>> exists, string fallback = "sector")
    {
        var baseSlug = Slugify.ToSlug(baseName, fallback);
        var slug = baseSlug;
        var suffix = 2;
        while (await exists(slug))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }
        return slug;
    }
}
