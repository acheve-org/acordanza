using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;

namespace Acordanza.Worker.Tests.Infrastructure;

// Trimmed down from Acordanza.Api.Tests' SeedData: the worker never authenticates
// anyone, so there's no TestUsers/UserManager/JWT here — just enough of a real
// ApplicationUser row to satisfy the AddedByUserId/UploadedByUserId foreign keys.
public static class SeedData
{
    public static async Task<ApplicationUser> CreateUserAsync(AppDbContext db, string name = "Test User")
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{Guid.NewGuid():N}@example.com",
            Email = $"{Guid.NewGuid():N}@example.com",
            Name = name
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    public static async Task<Area> CreateAreaAsync(AppDbContext db, string name)
    {
        var area = new Area
        {
            Name = name,
            Slug = Slugify.ToSlug(name),
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow
        };
        db.Areas.Add(area);
        await db.SaveChangesAsync();
        return area;
    }

    public static async Task<ClimbingRoute> CreateRouteAsync(
        AppDbContext db, Area area, string addedByUserId, string name = "Test Route", bool hasTopo = false)
    {
        var route = new ClimbingRoute
        {
            Name = name,
            Slug = $"{Slugify.ToSlug(name)}-{Guid.NewGuid():N}",
            AreaId = area.Id,
            Grade = "6a",
            Length = 100,
            Description = "",
            AddedByUserId = addedByUserId,
            ValidationStatus = ReviewStatus.Approved,
            DecidedAt = DateTimeOffset.UtcNow
        };
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
}
