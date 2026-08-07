using System.Net.Http.Headers;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Data;
using Acordanza.Api.Dtos;
using Acordanza.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Acordanza.Api.Tests.Infrastructure;

// Seeds a user in a role and hands back an HttpClient carrying a real JWT obtained
// through POST /api/auth/login — deliberately not minted directly, so the claim set
// under test (JwtTokenService + DynamicPermissions together) is the one production
// issues. See plan 08's "Authentication in tests".
public static class TestUsers
{
    private const string Password = "Test1234!";

    public static async Task<TestUser> CreateAsync(
        WebApplicationFactory<Program> factory, string role = Roles.User, string name = "Test User")
    {
        var (userId, email) = await CreateUserAsync(factory, role, name);
        var client = await LoginAsync(factory, email);
        return new TestUser(userId, email, client);
    }

    // A user linked to a FirstAscensionist record via an (implicitly approved) claim —
    // grants route.edit/route.validate/first-ascensionist.edit dynamically (see
    // DynamicPermissions). The link must exist before login, since the JWT's permission
    // claims are resolved at token-issue time.
    public static async Task<TestUser> CreateFirstAscensionistLinkedAsync(
        WebApplicationFactory<Program> factory, string firstAscensionistName, string role = Roles.User)
    {
        var (userId, email) = await CreateUserAsync(factory, role, firstAscensionistName);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FirstAscensionists.Add(new FirstAscensionist { Name = firstAscensionistName, UserId = userId });
            await db.SaveChangesAsync();
        }

        var client = await LoginAsync(factory, email);
        return new TestUser(userId, email, client);
    }

    // An approved AreaExpert of `areaId` (must be a top-level area — see
    // AreaExpertsController.RequestExpert). Unlike the first-ascensionist grant above,
    // this isn't a JWT claim (see AreaExpertAuthorizer) so it doesn't need to exist
    // before login, but seeding it before returning keeps the two overloads symmetric.
    public static async Task<TestUser> CreateAreaExpertAsync(
        WebApplicationFactory<Program> factory, int areaId, string role = Roles.User)
    {
        var (userId, email) = await CreateUserAsync(factory, role, "Area Expert");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AreaExperts.Add(new AreaExpert
            {
                AreaId = areaId,
                UserId = userId,
                Status = ReviewStatus.Approved,
                DecidedAt = DateTimeOffset.UtcNow,
                DecidedByUserId = userId
            });
            await db.SaveChangesAsync();
        }

        var client = await LoginAsync(factory, email);
        return new TestUser(userId, email, client);
    }

    private static async Task<(string UserId, string Email)> CreateUserAsync(
        WebApplicationFactory<Program> factory, string role, string name)
    {
        var email = $"{Guid.NewGuid():N}@example.test";

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = email, Email = email, Name = name };
        var createResult = await userManager.CreateAsync(user, Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create test user: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
        }

        var roleResult = await userManager.AddToRoleAsync(user, role);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to assign role '{role}': {string.Join(", ", roleResult.Errors.Select(e => e.Description))}");
        }

        return (user.Id, email);
    }

    private static async Task<HttpClient> LoginAsync(WebApplicationFactory<Program> factory, string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, Password));
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        if (body?.Token is null)
        {
            throw new InvalidOperationException("Login did not return a token (unexpected MFA challenge?).");
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.Token);
        return client;
    }
}

// IDisposable purely to release the HttpClient/handler pair returned by LoginAsync;
// disposing it doesn't tear down any Testcontainers state (that's ApiFixture's job).
public sealed record TestUser(string UserId, string Email, HttpClient Client) : IDisposable
{
    public void Dispose() => Client.Dispose();
}
