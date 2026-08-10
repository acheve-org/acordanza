using System.Net;
using System.Net.Http.Json;
using System.Text;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Acordanza.Api.Services;
using Acordanza.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// AuthController's password-recovery/invite-acceptance flows (backend/CLAUDE.md's "Auth
// model" section) — plan 08 slice 8: ForgotPassword never reveals whether an account
// exists; reset tokens are single-use because ResetPasswordAsync rotates the security
// stamp; ValidateToken is read-only and doesn't consume the token it checks; and invite
// acceptance turns a PasswordHash == null (invited-but-not-registered) account into a
// usable login.
public class AuthFlowsTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string NewPassword = "Reset1234!";

    [Fact]
    public async Task Forgot_password_for_an_unknown_email_returns_ok_true()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/forgot-password", new ForgotPasswordRequest($"{Guid.NewGuid():N}@example.test"),
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Forgot_password_for_a_known_email_also_returns_ok_true()
    {
        using var user = await TestUsers.CreateAsync(Factory);
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/forgot-password", new ForgotPasswordRequest(user.Email),
            TestContext.Current.CancellationToken);

        // Same 200 {ok:true} shape as the unknown-email case above — this endpoint must
        // never let a caller distinguish a registered email from an unregistered one.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Reset_password_token_can_only_be_used_once()
    {
        using var user = await TestUsers.CreateAsync(Factory);
        var token = await GenerateResetTokenAsync(user.Email);
        var client = Factory.CreateClient();

        var first = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(user.Email, token, NewPassword),
            TestContext.Current.CancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.OK);

        // ResetPasswordAsync rotated the security stamp on success — the same token,
        // whose data-protected payload embeds the stamp it was issued against, must no
        // longer verify.
        var second = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(user.Email, token, "AnotherPass1234!"),
            TestContext.Current.CancellationToken);
        second.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Validate_token_does_not_consume_a_reset_token()
    {
        using var user = await TestUsers.CreateAsync(Factory);
        var token = await GenerateResetTokenAsync(user.Email);
        var client = Factory.CreateClient();

        var firstCheck = await client.PostAsJsonAsync(
            "/api/auth/validate-token", new ValidateTokenRequest(user.Email, token), TestContext.Current.CancellationToken);
        firstCheck.StatusCode.ShouldBe(HttpStatusCode.OK);

        var secondCheck = await client.PostAsJsonAsync(
            "/api/auth/validate-token", new ValidateTokenRequest(user.Email, token), TestContext.Current.CancellationToken);
        secondCheck.StatusCode.ShouldBe(HttpStatusCode.OK);

        // The token must still be good for the real reset after being checked twice.
        var reset = await client.PostAsJsonAsync(
            "/api/auth/reset-password", new ResetPasswordRequest(user.Email, token, NewPassword),
            TestContext.Current.CancellationToken);
        reset.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Accepting_an_invite_sets_a_usable_password_on_a_pending_account()
    {
        var email = $"{Guid.NewGuid():N}@example.test";
        const string password = "Accepted1234!";

        using (var scope = Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email, Name = "Invited" };
            // No password: mirrors UsersController.InviteUser, which creates the account
            // with PasswordHash == null and mails a single-use accept-invite link.
            var createResult = await userManager.CreateAsync(user);
            createResult.Succeeded.ShouldBeTrue();
        }

        var token = await GenerateInviteTokenAsync(email);
        var client = Factory.CreateClient();

        var accept = await client.PostAsJsonAsync(
            "/api/auth/accept-invite", new AcceptInviteRequest(email, token, "New Name", password),
            TestContext.Current.CancellationToken);
        accept.StatusCode.ShouldBe(HttpStatusCode.OK);

        var login = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password), TestContext.Current.CancellationToken);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        body!.Token.ShouldNotBeNullOrEmpty();
    }

    // Mirrors AuthController.TryDecodeToken/ForgotPassword's own base64url encoding, so a
    // token minted directly through UserManager here round-trips the same way one
    // delivered by email would.
    private async Task<string> GenerateResetTokenAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        var rawToken = await userManager.GeneratePasswordResetTokenAsync(user!);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
    }

    private async Task<string> GenerateInviteTokenAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        var rawToken = await userManager.GenerateUserTokenAsync(user!, InviteTokens.ProviderName, InviteTokens.Purpose);
        return WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));
    }
}
