using Acordanza.Api.Dtos;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Acordanza.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Services;

// CreateRoute and UpdateRoute (RoutesController) validate and normalize the same
// UpsertRouteRequest fields line for line — this is that shared pass, hoisted out once
// the duplication reached its third divergent edit (plan 34's "grade is now optional"
// landed as the same change twice). Everything here is static/side-effect-free except
// the area-exists and first-ascensionist-resolution checks, which need db.
public static class RouteWrite
{
    public static readonly HashSet<string> AllowedOrientations =
        ["north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest"];

    public static readonly HashSet<string> AllowedGear = ["bolted", "removable", "pitons", "aiders"];

    private const int ObligatoryGradeMaxLength = 20;
    private const int GearNoteMaxLength = 500;

    public sealed record Values(
        string? Grade,
        string? AidGrade,
        string? Orientation,
        string? Gear,
        string Description,
        int? Length,
        string? ObligatoryGrade,
        string? RecommendedGear,
        string? OptionalGear,
        List<string> Links,
        List<FirstAscensionist> FirstAscensionists);

    // requireAreaId is CreateRoute's one extra check UpdateRoute never had (AreaId is
    // already known — an id in the URL — by the time a PUT reaches here); everything else
    // is identical between the two callers, including check order.
    public static async Task<(Values? Values, string? Error)> TryBuildAsync(
        AppDbContext db, UpsertRouteRequest request, bool requireAreaId = false)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return (null, "el nombre es obligatorio");
        }

        if (requireAreaId && request.AreaId == 0)
        {
            return (null, "el sector es obligatorio");
        }

        if (!await db.Areas.AnyAsync(a => a.Id == request.AreaId))
        {
            return (null, "el sector no existe");
        }

        // Absent is legal now (plan 34); present-but-wrong still isn't.
        if (!string.IsNullOrWhiteSpace(request.Grade) && !GradeValidator.IsValid(request.Grade))
        {
            return (null, "el grado debe seguir la escala francesa (por ejemplo 6a, 7c+, 8a)");
        }
        if (!string.IsNullOrWhiteSpace(request.AidGrade) && !AidGradeValidator.IsValid(request.AidGrade))
        {
            return (null, "el grado artificial debe ser A0–A5 o C0–C5 (por ejemplo A1, C2, A3+)");
        }

        var orientationError = ValidateOrientation(request.Orientation);
        if (orientationError is not null) return (null, orientationError);

        var gearError = ValidateGear(request.Gear);
        if (gearError is not null) return (null, gearError);

        var lengthError = ValidateLength(request.Length);
        if (lengthError is not null) return (null, lengthError);

        var obligatoryGrade = NormalizeOptionalText(request.ObligatoryGrade);
        var recommendedGear = NormalizeOptionalText(request.RecommendedGear);
        var optionalGear = NormalizeOptionalText(request.OptionalGear);
        var textLengthError = ValidateTextLength(obligatoryGrade, ObligatoryGradeMaxLength)
            ?? ValidateTextLength(recommendedGear, GearNoteMaxLength)
            ?? ValidateTextLength(optionalGear, GearNoteMaxLength);
        if (textLengthError is not null) return (null, textLengthError);

        var (links, linksError) = LinkRules.Resolve(request.Links);
        if (linksError is not null) return (null, linksError);

        var (firstAscensionists, firstAscensionistsError) = await ResolveFirstAscensionistsAsync(db, request.FirstAscensionistIds);
        if (firstAscensionistsError is not null) return (null, firstAscensionistsError);

        return (new Values(
            NormalizeOptionalGrade(request.Grade),
            NormalizeOptionalAidGrade(request.AidGrade),
            request.Orientation,
            request.Gear,
            request.Description ?? "",
            request.Length,
            obligatoryGrade,
            recommendedGear,
            optionalGear,
            links!,
            firstAscensionists!), null);
    }

    private static string? ValidateOrientation(string? orientation) =>
        orientation is not null && !AllowedOrientations.Contains(orientation)
            ? "la orientación no es válida"
            : null;

    private static string? ValidateGear(string? gear) =>
        gear is not null && !AllowedGear.Contains(gear)
            ? "el tipo de equipamiento no es válido"
            : null;

    private static string? ValidateLength(int? length) =>
        length is null or (> 5 and < 3000) ? null : "la longitud debe ser mayor que 5 y menor que 3000";

    private static string? ValidateTextLength(string? value, int maxLength) =>
        value is not null && value.Length > maxLength ? "el texto es demasiado largo" : null;

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeOptionalGrade(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : GradeValidator.Normalize(value);

    private static string? NormalizeOptionalAidGrade(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : AidGradeValidator.Normalize(value);

    private static async Task<(List<FirstAscensionist>? FirstAscensionists, string? Error)> ResolveFirstAscensionistsAsync(
        AppDbContext db, List<int>? firstAscensionistIds)
    {
        if (firstAscensionistIds is not { Count: > 0 }) return ([], null);

        var distinctIds = firstAscensionistIds.Distinct().ToList();
        var firstAscensionists = await db.FirstAscensionists.Where(o => distinctIds.Contains(o.Id)).ToListAsync();
        if (firstAscensionists.Count != distinctIds.Count)
        {
            return (null, "algún aperturista no existe");
        }

        return (firstAscensionists, null);
    }
}
