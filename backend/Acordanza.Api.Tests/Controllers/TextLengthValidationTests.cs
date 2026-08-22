using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Acordanza.Api.Authorization;
using Acordanza.Api.Dtos;
using Acordanza.Api.Tests.Infrastructure;
using Acordanza.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Acordanza.Api.Tests.Controllers;

// SEC-7: Area.Name, FirstAscensionist.Name and Topo.FileName had no length check on the
// write path at all (unlike RecommendedGear/OptionalGear/Link.Url, already validated
// before this) — an unbounded write up to Kestrel's body cap. Route.Name/.Description get
// the same treatment, covered in RouteRequiredFieldsTests instead since that file already
// owns Route field validation.
public class TextLengthValidationTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record ErrorResponse(string Error);

    [Fact]
    public async Task Creating_an_area_with_a_name_over_200_characters_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.PostAsJsonAsync(
            "/api/areas", new { name = new string('a', 201) }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el nombre es demasiado largo");
    }

    [Fact]
    public async Task Creating_a_first_ascensionist_with_a_name_over_200_characters_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        var response = await user.Client.PostAsJsonAsync(
            "/api/first-ascensionists", new { name = new string('a', 201) }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el nombre es demasiado largo");
    }

    [Fact]
    public async Task Uploading_a_topo_with_a_filename_over_255_characters_is_rejected()
    {
        using var user = await TestUsers.CreateAsync(Factory, Roles.User);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var area = await SeedData.CreateAreaAsync(db, $"Area {Guid.NewGuid():N}");
        var route = await SeedData.CreateRouteAsync(db, area, user.UserId);

        var file = new ByteArrayContent([0xFF, 0xD8, 0xFF, 0xD9]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        var overlongFileName = new string('a', 252) + ".jpg"; // 256 chars
        using var content = new MultipartFormDataContent { { file, "file", overlongFileName } };

        var response = await user.Client.PostAsync(
            $"/api/routes/{route.Id}/topos", content, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        body!.Error.ShouldBe("el nombre del archivo es demasiado largo");
    }
}
