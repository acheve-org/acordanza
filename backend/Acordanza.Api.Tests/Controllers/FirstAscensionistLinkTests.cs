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

// One first-ascensionist claim per user (plan 15, §5) — this rule wasn't enforced at
// all before this plan: RequestLink only rejected a duplicate pending request for the
// *same* aperturista, and Approve only guarded the target being unclaimed, not the
// requester. FirstAscensionistLinksController.RequestLink/Approve now both check.
public class FirstAscensionistLinkTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RequestLink_400s_when_the_caller_already_holds_a_pending_request_for_a_different_aperturista()
    {
        using var user = await TestUsers.CreateAsync(Factory);

        int firstId, secondId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            firstId = (await SeedData.CreateFirstAscensionistAsync(db, "Alex Aperturista")).Id;
            secondId = (await SeedData.CreateFirstAscensionistAsync(db, "Jane Doe")).Id;
        }

        var first = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(firstId),
            TestContext.Current.CancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // This is the case that fails against `main` today — a pending request for a
        // *different* aperturista wasn't blocked by the old same-aperturista-only check.
        var second = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(secondId),
            TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestLink_400s_when_the_caller_is_already_linked_to_an_aperturista()
    {
        using var user = await TestUsers.CreateFirstAscensionistLinkedAsync(Factory, "Alex Aperturista");

        int otherId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            otherId = (await SeedData.CreateFirstAscensionistAsync(db, "Jane Doe")).Id;
        }

        var response = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(otherId),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RequestLink_succeeds_after_a_prior_request_was_rejected()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var user = await TestUsers.CreateAsync(Factory);

        int firstId, secondId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            firstId = (await SeedData.CreateFirstAscensionistAsync(db, "Alex Aperturista")).Id;
            secondId = (await SeedData.CreateFirstAscensionistAsync(db, "Jane Doe")).Id;
        }

        var firstResponse = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(firstId),
            TestContext.Current.CancellationToken);
        var firstRequest = await firstResponse.Content.ReadFromJsonAsync<FirstAscensionistLinkRequestDto>(
            TestContext.Current.CancellationToken);

        var rejectResponse = await moderator.Client.PostAsJsonAsync(
            $"/api/first-ascensionist-links/{firstRequest!.Id}/reject",
            new RejectFirstAscensionistLinkRequest("no era la persona correcta"),
            TestContext.Current.CancellationToken);
        rejectResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The rule doesn't stick after a rejection — otherwise one mistaken claim would
        // lock the account out of ever claiming anyone, including the right person.
        var secondResponse = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(secondId),
            TestContext.Current.CancellationToken);
        secondResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Approving_a_second_pending_request_for_a_user_already_linked_in_the_meantime_400s_and_auto_rejects()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var user = await TestUsers.CreateAsync(Factory);

        int firstId, secondId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            firstId = (await SeedData.CreateFirstAscensionistAsync(db, "Alex Aperturista")).Id;
            secondId = (await SeedData.CreateFirstAscensionistAsync(db, "Jane Doe")).Id;
        }

        var firstResponse = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(firstId),
            TestContext.Current.CancellationToken);
        var firstRequest = await firstResponse.Content.ReadFromJsonAsync<FirstAscensionistLinkRequestDto>(
            TestContext.Current.CancellationToken);

        // Insert a second pending request directly — RequestLink itself would now
        // refuse to create this, but existing rows (from before this rule started being
        // enforced) can still be pending when a moderator comes to decide them.
        int secondRequestId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var secondRequest = new FirstAscensionistLinkRequest { FirstAscensionistId = secondId, UserId = user.UserId };
            db.FirstAscensionistLinkRequests.Add(secondRequest);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            secondRequestId = secondRequest.Id;
        }

        var approveFirst = await moderator.Client.PostAsync(
            $"/api/first-ascensionist-links/{firstRequest!.Id}/approve", null,
            TestContext.Current.CancellationToken);
        approveFirst.StatusCode.ShouldBe(HttpStatusCode.OK);

        var approveSecond = await moderator.Client.PostAsync(
            $"/api/first-ascensionist-links/{secondRequestId}/approve", null,
            TestContext.Current.CancellationToken);
        approveSecond.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var secondLinkRequest = await db.FirstAscensionistLinkRequests
                .SingleAsync(l => l.Id == secondRequestId, TestContext.Current.CancellationToken);
            secondLinkRequest.Status.ShouldBe(ReviewStatus.Rejected);

            var second = await db.FirstAscensionists
                .SingleAsync(f => f.Id == secondId, TestContext.Current.CancellationToken);
            second.UserId.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_first_claim_requests_and_approves_cleanly_and_links_the_first_ascensionist()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);
        using var user = await TestUsers.CreateAsync(Factory);

        int firstAscensionistId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            firstAscensionistId = (await SeedData.CreateFirstAscensionistAsync(db, "Alex Aperturista")).Id;
        }

        var requestResponse = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionist-links", new CreateFirstAscensionistLinkRequest(firstAscensionistId),
            TestContext.Current.CancellationToken);
        requestResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var request = await requestResponse.Content.ReadFromJsonAsync<FirstAscensionistLinkRequestDto>(
            TestContext.Current.CancellationToken);

        var approveResponse = await moderator.Client.PostAsync(
            $"/api/first-ascensionist-links/{request!.Id}/approve", null,
            TestContext.Current.CancellationToken);
        approveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        using var verifyScope = Factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstAscensionist = await verifyDb.FirstAscensionists
            .SingleAsync(f => f.Id == firstAscensionistId, TestContext.Current.CancellationToken);
        firstAscensionist.UserId.ShouldBe(user.UserId);
    }
}
