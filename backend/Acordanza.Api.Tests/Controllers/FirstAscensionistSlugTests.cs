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

namespace Acordanza.Api.Tests.Controllers;

// FirstAscensionist.Slug (issue #137): GenerateUniqueSlugAsync's "-2"/"-3" suffix
// disambiguation (the primary mechanism here, since names aren't unique — see plan
// 13), GET by-slug, slug regeneration on rename, and the accent/punctuation round-trip
// through Slugify.ToSlug. All requests run as a moderator so creates auto-validate.
public class FirstAscensionistSlugTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Creating_two_first_ascensionists_with_the_same_name_disambiguates_the_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        var first = await CreateAsync(moderator, "José García");
        var second = await CreateAsync(moderator, "José García");

        first.Slug.ShouldBe("jose-garcia");
        second.Slug.ShouldBe("jose-garcia-2");
    }

    [Fact]
    public async Task GetBySlug_returns_the_right_profile_for_each_and_404s_for_an_unknown_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        var first = await CreateAsync(moderator, "José García");
        var second = await CreateAsync(moderator, "José García");

        var firstProfile = await moderator.Client.GetFromJsonAsync<FirstAscensionistProfileDto>(
            $"/api/first-ascensionists/by-slug/{first.Slug}", TestContext.Current.CancellationToken);
        var secondProfile = await moderator.Client.GetFromJsonAsync<FirstAscensionistProfileDto>(
            $"/api/first-ascensionists/by-slug/{second.Slug}", TestContext.Current.CancellationToken);

        firstProfile!.Id.ShouldBe(first.Id);
        secondProfile!.Id.ShouldBe(second.Id);

        var missing = await moderator.Client.GetAsync(
            "/api/first-ascensionists/by-slug/nadie", TestContext.Current.CancellationToken);
        missing.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Renaming_regenerates_the_slug_and_the_old_one_404s()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        var created = await CreateAsync(moderator, "Hugo Biarge");
        var oldSlug = created.Slug;

        var updateResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{created.Id}",
            new UpdateFirstAscensionistRequest("Hugo Biarge Renamed"),
            TestContext.Current.CancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var newSlugResponse = await moderator.Client.GetAsync(
            "/api/first-ascensionists/by-slug/hugo-biarge-renamed", TestContext.Current.CancellationToken);
        newSlugResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var oldSlugResponse = await moderator.Client.GetAsync(
            $"/api/first-ascensionists/by-slug/{oldSlug}", TestContext.Current.CancellationToken);
        oldSlugResponse.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Saving_the_same_name_twice_keeps_the_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        var created = await CreateAsync(moderator, "Ana Ruiz");

        var updateResponse = await moderator.Client.PutAsJsonAsync(
            $"/api/first-ascensionists/{created.Id}",
            new UpdateFirstAscensionistRequest("Ana Ruiz"),
            TestContext.Current.CancellationToken);
        updateResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var profile = await moderator.Client.GetFromJsonAsync<FirstAscensionistProfileDto>(
            $"/api/first-ascensionists/by-slug/{created.Slug}", TestContext.Current.CancellationToken);
        profile!.Id.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Accents_and_punctuation_round_trip_through_the_slug()
    {
        using var moderator = await TestUsers.CreateAsync(Factory, Roles.Moderator);

        var created = await CreateAsync(moderator, "Peña Rúa");

        created.Slug.ShouldBe("pena-rua");
    }

    private async Task<(int Id, string Slug)> CreateAsync(TestUser user, string name)
    {
        var response = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionists", new CreateFirstAscensionistRequest(name), TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<FirstAscensionistDto>(TestContext.Current.CancellationToken);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var slug = await db.FirstAscensionists
            .Where(f => f.Id == dto!.Id)
            .Select(f => f.Slug)
            .SingleAsync(TestContext.Current.CancellationToken);

        return (dto!.Id, slug);
    }
}
