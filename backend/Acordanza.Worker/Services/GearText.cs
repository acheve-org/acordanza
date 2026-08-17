namespace Acordanza.Worker.Services;

// The one defensive clamp RouteCandidate.Gear needs before it reaches RecommendedGear:
// trim, treat whitespace-only as absent, and cap at RoutesController's 500-character limit
// on RecommendedGear. Shared by both readers rather than duplicated, mirroring how
// PrintedGrade/RouteImportService.AcceptableLength already centralise their own clamps.
// The prompt asks the model for brevity, so hitting the cap should be rare in practice —
// this is a "don't trust the model" backstop, not the primary control.
public static class GearText
{
    public static string? Truncate(string? gear, int maxLength = 500)
    {
        if (string.IsNullOrWhiteSpace(gear)) return null;

        var trimmed = gear.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
