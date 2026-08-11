using Acordanza.Api.Data;
using Acordanza.Api.Models;

namespace Acordanza.Api.Tests.Infrastructure;

// Small, composable helpers for seeding area trees, routes and first ascensionists
// directly through AppDbContext — tests call these against a scope's db, then
// SaveChangesAsync themselves (or let the helper do it, see each method), keeping
// control over transaction boundaries with the caller.
public static class SeedData
{
    public static async Task<Area> CreateAreaAsync(
        AppDbContext db, string name, Area? parent = null, bool validated = true, string? validatedByUserId = null)
    {
        var area = new Area
        {
            Name = name,
            Slug = Acordanza.Api.Services.Slugify.ToSlug(name),
            ParentAreaId = parent?.Id
        };
        if (validated)
        {
            area.ValidationStatus = ReviewStatus.Approved;
            area.DecidedAt = DateTimeOffset.UtcNow;
            area.DecidedByUserId = validatedByUserId;
        }

        db.Areas.Add(area);
        await db.SaveChangesAsync();
        return area;
    }

    public static async Task<FirstAscensionist> CreateFirstAscensionistAsync(
        AppDbContext db, string name, bool validated = true, string? validatedByUserId = null)
    {
        var firstAscensionist = new FirstAscensionist
        {
            Name = name,
            Slug = Acordanza.Api.Services.Slugify.ToSlug(name, "aperturista")
        };
        if (validated)
        {
            firstAscensionist.ValidationStatus = ReviewStatus.Approved;
            firstAscensionist.DecidedAt = DateTimeOffset.UtcNow;
            firstAscensionist.DecidedByUserId = validatedByUserId;
        }

        db.FirstAscensionists.Add(firstAscensionist);
        await db.SaveChangesAsync();
        return firstAscensionist;
    }

    public static async Task<ClimbingRoute> CreateRouteAsync(
        AppDbContext db,
        Area area,
        string addedByUserId,
        string name = "Test Route",
        string grade = "6a",
        string? orientation = null,
        string? gear = null,
        int length = 100,
        string description = "",
        List<FirstAscensionist>? firstAscensionists = null,
        bool hasTopo = false,
        bool validated = true,
        string? validatedByUserId = null)
    {
        var route = new ClimbingRoute
        {
            Name = name,
            // Guid-suffixed rather than a plain Slugify.ToSlug(name) — routes are
            // frequently seeded with the same default name in the same area across many
            // tests, and Route.Slug is unique per (AreaId, Slug); tests exercising the
            // slug-generation logic itself go through RoutesController, not this helper.
            Slug = $"{Acordanza.Api.Services.Slugify.ToSlug(name)}-{Guid.NewGuid():N}",
            AreaId = area.Id,
            Grade = grade,
            Orientation = orientation,
            Gear = gear,
            Length = length,
            Description = description,
            AddedByUserId = addedByUserId,
            FirstAscensionists = firstAscensionists ?? []
        };
        if (validated)
        {
            route.ValidationStatus = ReviewStatus.Approved;
            route.DecidedAt = DateTimeOffset.UtcNow;
            route.DecidedByUserId = validatedByUserId;
        }

        db.Routes.Add(route);
        await db.SaveChangesAsync();

        if (hasTopo)
        {
            db.Topos.Add(new Topo
            {
                RouteId = route.Id,
                BlobName = $"test/{route.Id}-01.jpg",
                ContentType = "image/jpeg",
                FileName = "topo.jpg",
                UploadedByUserId = addedByUserId
            });
            await db.SaveChangesAsync();
        }

        return route;
    }

    public static async Task<Tick> CreateTickAsync(
        AppDbContext db, ClimbingRoute route, string climberUserId, string date = "2024-01-01",
        int? rating = null, string notes = "")
    {
        var tick = new Tick
        {
            RouteId = route.Id,
            ClimberUserId = climberUserId,
            Date = date,
            Rating = rating,
            Notes = notes
        };
        db.Ticks.Add(tick);
        await db.SaveChangesAsync();
        return tick;
    }
}
