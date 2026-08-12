using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Core.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Acordanza.Core.Models;
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

    // Both edit and validate an approved record, but only one of them auto-revalidates it:
    // a moderator holds first-ascensionist.validate too (see RolePermissions), so their own
    // edit re-approves rather than falls back to Pending, the same rule
    // CreateFirstAscensionist already applies. A self-linked user, by contrast, is granted
    // first-ascensionist.edit but not .validate (see DynamicPermissions), so their edit still
    // demotes their own record to Pending — this is the one reachable case in the app where
    // "can edit" and "can validate" diverge, since role-granted edit/validate always travel
    // together (see RouteValidationLifecycleTests' equivalent note for routes).
    [Fact]
    public async Task Moderator_edit_keeps_first_ascensionist_approved()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        int firstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await SeedData.CreateFirstAscensionistAsync(db, "Approved Opener", validated: true);
            firstAscensionistId = opener.Id;
        }

        var response = await moderator.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{firstAscensionistId}",
            new UpdateFirstAscensionistRequest("Approved Opener Renamed"),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await db.FirstAscensionists.SingleAsync(f => f.Id == firstAscensionistId, TestContext.Current.CancellationToken);
            opener.ValidationStatus.ShouldBe(ReviewStatus.Approved);
            opener.DecidedAt.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task Linked_user_edit_of_their_own_approved_record_still_falls_back_to_pending()
    {
        using var linked = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Own Approved Opener");
        int firstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await db.FirstAscensionists.SingleAsync(f => f.Name == "Own Approved Opener", TestContext.Current.CancellationToken);
            opener.ValidationStatus = ReviewStatus.Approved;
            opener.DecidedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            firstAscensionistId = opener.Id;
        }

        var response = await linked.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{firstAscensionistId}",
            new UpdateFirstAscensionistRequest("Own Approved Opener Renamed"),
            TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opener = await db.FirstAscensionists.SingleAsync(f => f.Id == firstAscensionistId, TestContext.Current.CancellationToken);
            opener.ValidationStatus.ShouldBe(ReviewStatus.Pending);
            opener.DecidedAt.ShouldBeNull();
        }
    }

    private async Task<int> GetFirstAscensionistIdByNameAsync(string name)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.FirstAscensionists.SingleAsync(f => f.Name == name)).Id;
    }
}
