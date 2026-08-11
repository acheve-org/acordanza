using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// Plan 19 (.claude/plans/19-contributions-pagination.md, issue #149): GET /contributions/mine
// split into three paginated per-kind endpoints. The paths carry no "mine" segment, so the
// caller-scoping these tests pin is the only thing keeping one user's contributions out of
// another's list.
public class ContributionsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Each_endpoint_returns_only_the_callers_own_rows()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var stranger = await TestUsers.CreateAsync(Factory, Roles.User);

        int strangerRouteId, strangerAreaId, strangerOpenerId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            var route = await SeedData.CreateRouteAsync(db, area, stranger.UserId, name: "Vía ajena");
            strangerRouteId = route.Id;
            strangerAreaId = await SeedAreaAddedByAsync(db, stranger.UserId, "Sector ajeno");
            strangerOpenerId = await SeedFirstAscensionistAddedByAsync(db, stranger.UserId, "Aperturista ajeno");
        }

        var routes = await GetPageAsync(author, "routes");
        var areas = await GetPageAsync(author, "areas");
        var openers = await GetPageAsync(author, "first-ascensionists");

        routes.Items.ShouldNotContain(c => c.Id == strangerRouteId);
        routes.TotalCount.ShouldBe(0);
        areas.Items.ShouldNotContain(c => c.Id == strangerAreaId);
        areas.TotalCount.ShouldBe(0);
        openers.Items.ShouldNotContain(c => c.Id == strangerOpenerId);
        openers.TotalCount.ShouldBe(0);

        var strangerRoutes = await GetPageAsync(stranger, "routes");
        strangerRoutes.Items.ShouldContain(c => c.Id == strangerRouteId);
    }

    [Fact]
    public async Task Routes_page_through_25_rows_without_gaps_or_duplicates()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            for (var i = 0; i < 25; i++)
            {
                await SeedData.CreateRouteAsync(db, area, author.UserId, name: $"Vía {i:D2}");
            }
        }

        var page1 = await GetPageAsync(author, "routes", page: 1);
        var page2 = await GetPageAsync(author, "routes", page: 2);
        var page3 = await GetPageAsync(author, "routes", page: 3);
        var page4 = await GetPageAsync(author, "routes", page: 4);

        page1.Items.Count.ShouldBe(10);
        page1.TotalCount.ShouldBe(25);
        page1.Page.ShouldBe(1);
        page1.PageSize.ShouldBe(10);
        page2.Items.Count.ShouldBe(10);
        page3.Items.Count.ShouldBe(5);
        page4.Items.ShouldBeEmpty();
        page4.TotalCount.ShouldBe(25);

        var seen = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(c => c.Id).ToList();
        seen.Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task Rows_sharing_a_CreatedAt_still_partition_cleanly_across_pages()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        // Every row gets the same CreatedAt, so ordering rests entirely on the Id tie-break.
        var sharedCreatedAt = DateTimeOffset.UtcNow;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            for (var i = 0; i < 15; i++)
            {
                var route = await SeedData.CreateRouteAsync(db, area, author.UserId, name: $"Vía {i:D2}");
                route.CreatedAt = sharedCreatedAt;
            }
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var page1 = await GetPageAsync(author, "routes", page: 1);
        var page2 = await GetPageAsync(author, "routes", page: 2);

        page1.Items.Count.ShouldBe(10);
        page2.Items.Count.ShouldBe(5);
        page1.Items.Concat(page2.Items).Select(c => c.Id).Distinct().Count().ShouldBe(15);
    }

    [Fact]
    public async Task Routes_come_back_newest_first()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            var oldest = await SeedData.CreateRouteAsync(db, area, author.UserId, name: "La más vieja");
            oldest.CreatedAt = DateTimeOffset.UtcNow.AddDays(-10);
            var middle = await SeedData.CreateRouteAsync(db, area, author.UserId, name: "La de en medio");
            middle.CreatedAt = DateTimeOffset.UtcNow.AddDays(-5);
            var newest = await SeedData.CreateRouteAsync(db, area, author.UserId, name: "La más nueva");
            newest.CreatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var page = await GetPageAsync(author, "routes");

        page.Items.Select(c => c.Name).ShouldBe(["La más nueva", "La de en medio", "La más vieja"]);
    }

    [Fact]
    public async Task Out_of_range_paging_parameters_are_clamped_and_pageSize_is_honoured()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            for (var i = 0; i < 12; i++)
            {
                await SeedData.CreateRouteAsync(db, area, author.UserId, name: $"Vía {i:D2}");
            }
        }

        var zeroPage = await GetPageAsync(author, "routes", page: 0);
        zeroPage.Page.ShouldBe(1);
        zeroPage.Items.Count.ShouldBe(10);

        var zeroSize = await GetPageAsync(author, "routes", pageSize: 0);
        zeroSize.PageSize.ShouldBe(10);
        zeroSize.Items.Count.ShouldBe(10);

        var hugeSize = await GetPageAsync(author, "routes", pageSize: 1000);
        hugeSize.PageSize.ShouldBe(10);
        hugeSize.Items.Count.ShouldBe(10);

        var smallSize = await GetPageAsync(author, "routes", pageSize: 5);
        smallSize.PageSize.ShouldBe(5);
        smallSize.Items.Count.ShouldBe(5);
        smallSize.TotalCount.ShouldBe(12);
    }

    [Fact]
    public async Task A_rejected_route_keeps_its_status_reason_and_decider()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator, name: "Moderadora");
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
            var route = await SeedData.CreateRouteAsync(db, area, author.UserId, name: "Vía rechazada", validated: false);
            Validation.MarkRejected(route, moderator.UserId, "ya existe");
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var page = await GetPageAsync(author, "routes");

        var rejected = page.Items.ShouldHaveSingleItem();
        rejected.ValidationStatus.ShouldBe("rejected");
        rejected.RejectionReason.ShouldBe("ya existe");
        rejected.DecidedByName.ShouldBe("Moderadora");
        rejected.DecidedAt.ShouldNotBeNull();
        rejected.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Areas_and_first_ascensionists_carry_their_own_Detail_shape()
    {
        using var author = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedAreaAddedByAsync(db, author.UserId, "Sector propio");
            await SeedFirstAscensionistAddedByAsync(db, author.UserId, "Aperturista propio");
        }

        var areas = await GetPageAsync(author, "areas");
        var openers = await GetPageAsync(author, "first-ascensionists");

        areas.Items.ShouldHaveSingleItem().Detail.ShouldBe("Sector propio");
        var opener = openers.Items.ShouldHaveSingleItem();
        opener.Name.ShouldBe("Aperturista propio");
        opener.Detail.ShouldBeNull();
    }

    [Theory]
    [InlineData("routes")]
    [InlineData("areas")]
    [InlineData("first-ascensionists")]
    public async Task Anonymous_requests_are_rejected(string kind)
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.GetAsync($"/api/contributions/{kind}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private static async Task<PagedContributionsResponse> GetPageAsync(
        TestUser user, string kind, int? page = null, int? pageSize = null)
    {
        var query = new List<string>();
        if (page is int p) query.Add($"page={p}");
        if (pageSize is int s) query.Add($"pageSize={s}");
        var url = $"/api/contributions/{kind}{(query.Count > 0 ? "?" + string.Join("&", query) : "")}";

        return (await user.Client.GetFromJsonAsync<PagedContributionsResponse>(
            url, TestContext.Current.CancellationToken))!;
    }

    // SeedData's area/first-ascensionist helpers don't set AddedByUserId (nothing else needs
    // it), and it's the only column these endpoints filter on.
    private static async Task<int> SeedAreaAddedByAsync(AppDbContext db, string userId, string name)
    {
        var area = await SeedData.CreateAreaAsync(db, name);
        area.AddedByUserId = userId;
        await db.SaveChangesAsync();
        return area.Id;
    }

    private static async Task<int> SeedFirstAscensionistAddedByAsync(AppDbContext db, string userId, string name)
    {
        var firstAscensionist = await SeedData.CreateFirstAscensionistAsync(db, name);
        firstAscensionist.AddedByUserId = userId;
        await db.SaveChangesAsync();
        return firstAscensionist.Id;
    }
}
