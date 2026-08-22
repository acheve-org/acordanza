using System.Net;
using System.Net.Http.Json;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Acordanza.Core.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// SEC-4a: JwtTokenService embeds ApplicationUser.SecurityStamp in every token, and
// Program.cs's OnTokenValidated rejects a request whose token carries a stale stamp.
// ChangePasswordAsync/ResetPasswordAsync/ChangeEmailAsync all rotate the stamp already
// (see AuthFlowsTests) — this is what makes that rotation actually revoke the token
// instead of leaving it valid until it expires on its own.
public class SecurityStampRevocationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task A_token_issued_before_a_password_change_stops_working_after_it()
    {
        using var user = await TestUsers.CreateAsync(Factory);

        var before = await user.Client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        before.StatusCode.ShouldBe(HttpStatusCode.OK);

        await RotateSecurityStampAsync(user.Email);

        var after = await user.Client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        after.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logging_in_again_after_a_password_change_issues_a_working_token()
    {
        using var user = await TestUsers.CreateAsync(Factory);
        await RotateSecurityStampAsync(user.Email);

        var login = await Factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(user.Email, "Test1234!"), TestContext.Current.CancellationToken);
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await login.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", body!.Token);
        var me = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);
        me.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // Stands in for any of the three flows that already rotate the stamp (password
    // change, password reset, email change) — what's under test here is that the
    // token pipeline reacts to the rotation, not which flow causes it.
    private async Task RotateSecurityStampAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        await userManager.UpdateSecurityStampAsync(user!);
    }
}
