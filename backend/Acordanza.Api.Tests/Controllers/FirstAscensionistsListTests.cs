using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Core.Data;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// GET /api/first-ascensionists moved from an unpaginated array to PagedFirstAscensionistsResponse
// (page/pageSize, default 10) so FirstAscensionistsPage (/manage/first-ascensionists) and
// RequestFirstAscensionistLinkModal (/my-acordanza/claim-first-ascensionist) can page and
// search server-side instead of over a client-held array.
public class FirstAscensionistsListTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Openers_page_through_25_rows_without_gaps_or_duplicates()
    {
        using var caller = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 25; i++)
            {
                await SeedData.CreateFirstAscensionistAsync(db, $"Aperturista {i:D2}");
            }
        }

        var page1 = await GetPageAsync(caller);
        var page2 = await GetPageAsync(caller, page: 2);
        var page3 = await GetPageAsync(caller, page: 3);
        var page4 = await GetPageAsync(caller, page: 4);

        page1.Items.Count.ShouldBe(10);
        page1.TotalCount.ShouldBe(25);
        page1.Page.ShouldBe(1);
        page1.PageSize.ShouldBe(10);
        page2.Items.Count.ShouldBe(10);
        page3.Items.Count.ShouldBe(5);
        page4.Items.ShouldBeEmpty();
        page4.TotalCount.ShouldBe(25);

        var seen = page1.Items.Concat(page2.Items).Concat(page3.Items).Select(o => o.Id).ToList();
        seen.Distinct().Count().ShouldBe(25);
    }

    [Fact]
    public async Task Q_filters_by_name_server_side_and_totalCount_matches_the_filtered_set()
    {
        using var caller = await TestUsers.CreateAsync(Factory, Roles.User);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SeedData.CreateFirstAscensionistAsync(db, "Hugo Biarge");
            await SeedData.CreateFirstAscensionistAsync(db, "Hugo Ortiz");
            await SeedData.CreateFirstAscensionistAsync(db, "Alguien Distinto");
        }

        var filtered = await GetPageAsync(caller, q: "Hugo");

        filtered.TotalCount.ShouldBe(2);
        filtered.Items.ShouldAllBe(o => o.Name.Contains("Hugo"));
    }

    [Fact]
    public async Task UnlinkedOnly_excludes_openers_with_a_linked_account_from_items_and_totalCount()
    {
        using var caller = await TestUsers.CreateAsync(Factory, Roles.User);
        using var linkedUser = await TestUsers.CreateAsync(Factory, Roles.User, name: "Linked Person");
        int linkedId, unlinkedId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var linked = await SeedData.CreateFirstAscensionistAsync(db, $"Linked Opener {Guid.NewGuid():N}");
            linked.UserId = linkedUser.UserId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            linkedId = linked.Id;

            var unlinked = await SeedData.CreateFirstAscensionistAsync(db, $"Unlinked Opener {Guid.NewGuid():N}");
            unlinkedId = unlinked.Id;
        }

        var response = await caller.Client.GetFromJsonAsync<PagedFirstAscensionistsResponse>(
            "/api/first-ascensionists?unlinkedOnly=true", TestContext.Current.CancellationToken);

        response!.Items.ShouldContain(o => o.Id == unlinkedId);
        response.Items.ShouldNotContain(o => o.Id == linkedId);
    }

    private static async Task<PagedFirstAscensionistsResponse> GetPageAsync(
        TestUser user, string? q = null, int page = 1, int pageSize = 10)
    {
        var query = new List<string> { $"page={page}", $"pageSize={pageSize}" };
        if (q is not null) query.Add($"q={Uri.EscapeDataString(q)}");

        var response = await user.Client.GetFromJsonAsync<PagedFirstAscensionistsResponse>(
            $"/api/first-ascensionists?{string.Join("&", query)}", TestContext.Current.CancellationToken);
        return response!;
    }
}
