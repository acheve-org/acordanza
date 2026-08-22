using System.Net;
using System.Net.Http.Json;
using System.Text;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Core.Models;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// The self-service email-change flow (plan 14): POST /api/account/email/change-request
// mints an Identity ChangeEmail token embedding the new address, POST
// /api/auth/confirm-email-change spends it. The suite doesn't intercept outgoing mail
// (ApiFixture doesn't swap IEmailSender), so confirmation mints the same token the
// controller would, in-process via UserManager.GenerateChangeEmailTokenAsync — the
// technique UsersListTests already uses for invites.
public class EmailChangeTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Password = "Test1234!";

    [Fact]
    public async Task Request_with_wrong_password_is_rejected_and_leaves_the_address_unchanged()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var newEmail = $"{Guid.NewGuid():N}@example.test";

        var response = await user.Client.PostAsJsonAsync(
            "/api/account/email/change-request",
            new ChangeEmailRequest(newEmail, "wrong-password"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var me = await user.Client.GetFromJsonAsync<MeResponse>("/api/auth/me", TestContext.Current.CancellationToken);
        me!.Username.ShouldBe(user.Email);
    }

    [Fact]
    public async Task Request_to_an_address_another_account_already_holds_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        using var other = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.PostAsJsonAsync(
            "/api/account/email/change-request",
            new ChangeEmailRequest(other.Email, Password),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Happy_path_changes_the_address_and_updates_login()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var newEmail = $"{Guid.NewGuid():N}@example.test";

        var requestResponse = await user.Client.PostAsJsonAsync(
            "/api/account/email/change-request",
            new ChangeEmailRequest(newEmail, Password),
            TestContext.Current.CancellationToken);
        requestResponse.EnsureSuccessStatusCode();

        var encodedToken = await MintConfirmTokenAsync(user.UserId, newEmail);

        var confirmResponse = await user.Client.PostAsJsonAsync(
            "/api/auth/confirm-email-change",
            new ConfirmEmailChangeRequest(user.UserId, newEmail, encodedToken),
            TestContext.Current.CancellationToken);
        confirmResponse.EnsureSuccessStatusCode();

        // ChangeEmailAsync rotates the security stamp (SEC-4a), so the pre-change token
        // user.Client still carries is now revoked — /auth/me must be checked with a
        // freshly-issued one.
        using var freshClient = Factory.CreateClient();
        var newLogin = await freshClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(newEmail, Password), TestContext.Current.CancellationToken);
        newLogin.StatusCode.ShouldBe(HttpStatusCode.OK);
        var newLoginBody = await newLogin.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        freshClient.DefaultRequestHeaders.Authorization = new("Bearer", newLoginBody!.Token);

        var me = await freshClient.GetFromJsonAsync<MeResponse>("/api/auth/me", TestContext.Current.CancellationToken);
        me!.Username.ShouldBe(newEmail);

        var oldLogin = await freshClient.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(user.Email, Password), TestContext.Current.CancellationToken);
        oldLogin.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirming_the_same_link_twice_rejects_the_second_attempt()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var newEmail = $"{Guid.NewGuid():N}@example.test";
        var encodedToken = await MintConfirmTokenAsync(user.UserId, newEmail);

        var first = await user.Client.PostAsJsonAsync(
            "/api/auth/confirm-email-change",
            new ConfirmEmailChangeRequest(user.UserId, newEmail, encodedToken),
            TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var second = await user.Client.PostAsJsonAsync(
            "/api/auth/confirm-email-change",
            new ConfirmEmailChangeRequest(user.UserId, newEmail, encodedToken),
            TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_token_minted_for_one_address_cannot_confirm_a_different_one()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);
        var mintedFor = $"{Guid.NewGuid():N}@example.test";
        var usedFor = $"{Guid.NewGuid():N}@example.test";
        var encodedToken = await MintConfirmTokenAsync(user.UserId, mintedFor);

        var response = await user.Client.PostAsJsonAsync(
            "/api/auth/confirm-email-change",
            new ConfirmEmailChangeRequest(user.UserId, usedFor, encodedToken),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        using var anonymous = Factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(
            "/api/account/email/change-request",
            new ChangeEmailRequest("someone@example.test", Password),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    private async Task<string> MintConfirmTokenAsync(string userId, string newEmail)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByIdAsync(userId);
        var token = await userManager.GenerateChangeEmailTokenAsync(user!, newEmail);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));
    }
}
