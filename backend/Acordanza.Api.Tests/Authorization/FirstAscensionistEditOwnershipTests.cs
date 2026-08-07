using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Authorization;

// FirstAscensionistEdit is granted both to moderators/admins (any record) and,
// dynamically, to a user linked to their own FirstAscensionist record (see
// DynamicPermissions) — so a linked user editing someone else's record has to be
// blocked by an ownership check inside the controller, not by the policy alone (see
// backend/CLAUDE.md's note on FirstAscensionistsController.UpdateFirstAscensionist).
// The plain authorization matrix can't see this distinction since both cases carry
// the same claim — this test targets the ownership scoping specifically.
public class FirstAscensionistEditOwnershipTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Linked_user_can_edit_their_own_first_ascensionist_record()
    {
        using var linked = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Own Opener");
        var ownFirstAscensionistId = await GetFirstAscensionistIdByNameAsync("Own Opener");

        var response = await linked.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{ownFirstAscensionistId}",
            new UpdateFirstAscensionistRequest("Own Opener Renamed"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Linked_user_cannot_edit_someone_elses_first_ascensionist_record()
    {
        using var linked = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Linked Opener");

        int othersFirstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var other = await SeedData.CreateFirstAscensionistAsync(db, "Someone Else's Opener");
            othersFirstAscensionistId = other.Id;
        }

        var response = await linked.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{othersFirstAscensionistId}",
            new UpdateFirstAscensionistRequest("Hijacked Name"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Moderator_can_edit_any_first_ascensionist_record_regardless_of_link()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        int firstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await SeedData.CreateFirstAscensionistAsync(db, "Unlinked Opener");
            firstAscensionistId = opener.Id;
        }

        var response = await moderator.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{firstAscensionistId}",
            new UpdateFirstAscensionistRequest("Moderator Renamed"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<int> GetFirstAscensionistIdByNameAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.FirstAscensionists.SingleAsync(f => f.Name == name)).Id;
    }
}
