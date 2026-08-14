using Acordanza.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Core.Services;

// The one implementation of "give this route a slug that's free within its area".
// Hoisted out of RoutesController when plan 30 gave the worker a second route-creating
// path (RouteImportService) — same reason LinkRules was hoisted in plan 18: a second
// copy of a uniqueness loop is a second chance to get the scoping subtly wrong, and
// this one backs a URL.
public static class RouteSlugs
{
    // ClimbingRoute.Name has no uniqueness constraint at all (unlike Area, which enforces
    // it per sibling), so the "-2"/"-3" suffix loop is the primary disambiguator, not just
    // a diacritics fallback — two "Directa"s in the same area get directa and directa-2.
    // Scoped to AreaId, not global, mirroring AreasController.GenerateUniqueAreaSlugAsync
    // (per-parent) rather than FirstAscensionistsController's flat version — see
    // ClimbingRoute.Slug. excludeId lets an unchanged name/area keep its slug on update.
    public static async Task<string> GenerateUniqueAsync(
        AppDbContext db, int areaId, string name, int? excludeId = null, CancellationToken cancellationToken = default)
    {
        var baseSlug = Slugify.ToSlug(name);
        var slug = baseSlug;
        var suffix = 2;
        while (await db.Routes.AnyAsync(r => r.AreaId == areaId && r.Slug == slug && r.Id != excludeId, cancellationToken))
        {
            slug = $"{baseSlug}-{suffix}";
            suffix++;
        }
        return slug;
    }
}
