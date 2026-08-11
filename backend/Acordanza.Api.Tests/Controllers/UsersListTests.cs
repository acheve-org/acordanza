using System.Net.Http.Json;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Services;
using Acordanza.Core.Services;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// GET /api/users gaining invitedByName/invitedAt/confirmedAt (see plan 12) — the fields
// already stored on ApplicationUser but not previously returned, plus the new
// ConfirmedAt column set by AcceptInvite/create-user.
public class UsersListTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Invited_account_shows_inviter_name_and_invite_date_before_acceptance()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin, name: "Inviting Admin");
        using var directlySeeded = await TestUsers.CreateAsync(Factory, Roles.User, name: "Direct Seed");

        var email = $"{Guid.NewGuid():N}@example.test";
        var inviteResponse = await admin.Client.PostAsJsonAsync(
            "/api/users/invite", new InviteUserRequest(email, Roles.User), TestContext.Current.CancellationToken);
        inviteResponse.EnsureSuccessStatusCode();

        var users = await admin.Client.GetFromJsonAsync<PagedUsersResponse>(
            $"/api/users?q={Uri.EscapeDataString(email)}", TestContext.Current.CancellationToken);
        var invited = users!.Items.Single(u => u.Email == email);

        invited.InvitedByName.ShouldBe("Inviting Admin");
        invited.InvitedAt.ShouldNotBeNull();
        invited.Pending.ShouldBeTrue();
        invited.ConfirmedAt.ShouldBeNull();

        var directUsers = await admin.Client.GetFromJsonAsync<PagedUsersResponse>(
            $"/api/users?q={Uri.EscapeDataString(directlySeeded.Email)}", TestContext.Current.CancellationToken);
        var direct = directUsers!.Items.Single(u => u.Email == directlySeeded.Email);

        direct.InvitedByName.ShouldBeNull();
        direct.InvitedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Accepting_the_invite_sets_confirmedAt_and_clears_pending()
    {
        using var admin = await TestUsers.CreateAsync(Factory, Roles.Admin, name: "Inviting Admin");

        var email = $"{Guid.NewGuid():N}@example.test";
        var inviteResponse = await admin.Client.PostAsJsonAsync(
            "/api/users/invite", new InviteUserRequest(email, Roles.User), TestContext.Current.CancellationToken);
        inviteResponse.EnsureSuccessStatusCode();

        // Drives acceptance in-process without intercepting the invite email (ApiFixture
        // doesn't swap IEmailSender) — mints the same single-use token InviteUser would
        // have emailed, exactly as UsersController.InviteUser encodes it.
        string encodedToken;
        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email);
            var token = await userManager.GenerateUserTokenAsync(user!, InviteTokens.ProviderName, InviteTokens.Purpose);
            encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
        }

        var beforeAccept = DateTimeOffset.UtcNow;
        var acceptResponse = await admin.Client.PostAsJsonAsync(
            "/api/auth/accept-invite",
            new AcceptInviteRequest(email, encodedToken, "New Person", "Test1234!"),
            TestContext.Current.CancellationToken);
        acceptResponse.EnsureSuccessStatusCode();

        var users = await admin.Client.GetFromJsonAsync<PagedUsersResponse>(
            $"/api/users?q={Uri.EscapeDataString(email)}", TestContext.Current.CancellationToken);
        var accepted = users!.Items.Single(u => u.Email == email);

        accepted.Pending.ShouldBeFalse();
        accepted.ConfirmedAt.ShouldNotBeNull();
        accepted.ConfirmedAt!.Value.ShouldBeGreaterThanOrEqualTo(beforeAccept);
        accepted.ConfirmedAt!.Value.ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);
    }
}
