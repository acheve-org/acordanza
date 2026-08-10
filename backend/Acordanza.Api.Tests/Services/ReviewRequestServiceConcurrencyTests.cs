using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Services;

// ReviewRequestService's doc comment (Services/ReviewRequestService.cs) claims a
// concurrent double-decide is impossible: two callers racing to approve the same
// pending request settle into exactly one Applied and one AlreadyResolved, because the
// single conditional ExecuteUpdateAsync is scoped to Status == Pending and Postgres
// row-locks the update — the loser's WHERE clause simply no longer matches once the
// winner commits. An in-memory/fake context can't demonstrate row-level locking, so this
// is the one property in this plan that needs a real database — see plan 08 slice 6.
public class ReviewRequestServiceConcurrencyTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Two_concurrent_approvals_of_the_same_area_expert_request_yield_exactly_one_Applied()
    {
        using var requester = await TestUsers.CreateAsync(Factory, Roles.User);
        using var approver = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        int expertId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");

            var expert = new AreaExpert { AreaId = area.Id, UserId = requester.UserId };
            db.AreaExperts.Add(expert);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            expertId = expert.Id;
        }

        // Each racer gets its own scope/DbContext — DbContext isn't thread-safe, and the
        // point is two independent requests hitting Postgres concurrently, not two
        // awaits sharing one context.
        using var scopeA = Factory.Services.CreateScope();
        using var scopeB = Factory.Services.CreateScope();
        var dbA = scopeA.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbB = scopeB.ServiceProvider.GetRequiredService<AppDbContext>();

        var results = await Task.WhenAll(
            ReviewRequestService.ApproveAsync(dbA.AreaExperts, expertId, approver.UserId),
            ReviewRequestService.ApproveAsync(dbB.AreaExperts, expertId, approver.UserId));

        results.Count(r => r == DecisionOutcome.Applied).ShouldBe(1);
        results.Count(r => r == DecisionOutcome.AlreadyResolved).ShouldBe(1);
    }
}
