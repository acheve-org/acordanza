using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Data/AppDbContext.cs's OnModelCreating detail this plan calls out (slice 7): Area's
// unique (ParentAreaId, Name)/(ParentAreaId, Slug) indexes use AreNullsDistinct(false),
// so two root areas (both ParentAreaId == null) collide exactly like two siblings would —
// nulls are NOT treated as distinct the way Postgres normally would. The index is also
// case-exact (a plain column index, not an expression one), unlike
// AreasController.CreateArea's own ToLower() pre-check — these tests go straight through
// AppDbContext, bypassing the controller, to observe the raw index behaviour.
public class AreaTreeConstraintsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Two_root_areas_with_the_same_exact_case_name_collide()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Root {Guid.NewGuid():N}";

        db.Areas.Add(new Area { Name = name, Slug = $"root-{Guid.NewGuid():N}" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Areas.Add(new Area { Name = name, Slug = $"root-{Guid.NewGuid():N}" });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Two_root_areas_with_differently_cased_names_do_not_collide()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N");

        db.Areas.Add(new Area { Name = $"Root {suffix}", Slug = $"root-a-{suffix}" });
        db.Areas.Add(new Area { Name = $"ROOT {suffix}", Slug = $"root-b-{suffix}" });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.Areas.CountAsync(a => a.Name == $"Root {suffix}" || a.Name == $"ROOT {suffix}",
            TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Same_named_children_under_different_parents_do_not_collide()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parentA = await SeedData.CreateAreaAsync(db, $"Parent A {Guid.NewGuid():N}");
        var parentB = await SeedData.CreateAreaAsync(db, $"Parent B {Guid.NewGuid():N}");

        db.Areas.Add(new Area { Name = "Sub Area", Slug = "sub-area-a", ParentAreaId = parentA.Id });
        db.Areas.Add(new Area { Name = "Sub Area", Slug = "sub-area-b", ParentAreaId = parentB.Id });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await db.Areas.CountAsync(a => a.Name == "Sub Area", TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Two_root_areas_with_the_same_slug_collide()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = $"root-{Guid.NewGuid():N}";

        db.Areas.Add(new Area { Name = $"Name A {Guid.NewGuid():N}", Slug = slug });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        db.Areas.Add(new Area { Name = $"Name B {Guid.NewGuid():N}", Slug = slug });
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    // Area.ParentArea is DeleteBehavior.Restrict (AppDbContext) — deleting a parent while
    // a child still references it is rejected at the database level. The controller
    // never hits this path itself (AreasController.DeleteArea sweeps the whole subtree
    // via AreaHierarchy.DescendantIds in one RemoveRange), so this goes straight through
    // AppDbContext to prove the FK constraint is really there as a backstop.
    [Fact]
    public async Task Deleting_a_parent_area_with_a_child_still_attached_is_restricted()
    {
        int parentId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parent = await SeedData.CreateAreaAsync(db, $"Parent {Guid.NewGuid():N}");
            await SeedData.CreateAreaAsync(db, "Child", parent);
            parentId = parent.Id;
        }

        // A fresh scope that only ever loads the parent — unlike the seeding scope
        // above, it has no idea a child exists, so EF Core's own in-memory relationship
        // fixup (which would otherwise null out a tracked, still-referencing child's FK
        // rather than let the delete fail) can't kick in. This is the real path a stray
        // `db.Areas.Remove(area)` outside AreasController.DeleteArea's whole-subtree
        // sweep would take, and it's the DB's own "ON DELETE RESTRICT" that has to
        // reject it.
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parent = await db.Areas.FindAsync([parentId], TestContext.Current.CancellationToken);
            db.Areas.Remove(parent!);
            await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    // Topo.Route is DeleteBehavior.Cascade (AppDbContext) — the opposite end of the same
    // constraint spectrum: deleting the route deletes its topo rows for free at the
    // database level, no application-level cleanup needed for the DB row itself (only
    // the blob, which RoutesController.DeleteRoute handles separately).
    [Fact]
    public async Task Deleting_a_route_cascades_its_topos()
    {
        int routeId, topoId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var user = await TestUsers.CreateAsync(Factory, Roles.User);
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            var route = await SeedData.CreateRouteAsync(db, area, user.UserId, hasTopo: true);
            routeId = route.Id;
            topoId = await db.Topos.Where(t => t.RouteId == routeId).Select(t => t.Id)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var route = await db.Routes.FindAsync([routeId], TestContext.Current.CancellationToken);
            db.Routes.Remove(route!);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Topos.AnyAsync(t => t.Id == topoId, TestContext.Current.CancellationToken)).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task Slug_path_resolves_through_nested_areas()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var top = await SeedData.CreateAreaAsync(db, $"Top {Guid.NewGuid():N}");
        var sub = await SeedData.CreateAreaAsync(db, "Sub Sector", top);

        var response = await user.Client.GetAsync(
            $"/api/areas/by-slug-path/{top.Slug}/{sub.Slug}", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<AreaDetailDto>(TestContext.Current.CancellationToken);
        body!.Id.ShouldBe(sub.Id);
        body.Ancestors.ShouldHaveSingleItem();
        body.Ancestors[0].Id.ShouldBe(top.Id);
    }

    [Fact]
    public async Task Slug_path_with_a_wrong_segment_is_not_found()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var top = await SeedData.CreateAreaAsync(db, $"Top {Guid.NewGuid():N}");

        var response = await user.Client.GetAsync(
            $"/api/areas/by-slug-path/{top.Slug}/does-not-exist", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
