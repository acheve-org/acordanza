using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acordanza.Api.Dtos;

// Serializes an enum's members as their lowercase name (Orientation.North -> "north"),
// matching the wire values RoutesController accepted/returned when Orientation and Gear
// were still string allowlists (DUP-12) rather than real C# enums — every member name
// below is a single word, so JsonNamingPolicy.CamelCase (which only lowercases the
// leading character) reproduces those exact strings. Scoped to just these two enums via
// the [JsonConverter] attribute on each, rather than a global converter registration, so
// this is correct independently of whatever other enums this codebase later configures.
public sealed class LowercaseJsonStringEnumConverter<TEnum>()
    : JsonStringEnumConverter<TEnum>(JsonNamingPolicy.CamelCase)
    where TEnum : struct, Enum;

// The fixed set of route orientations RoutesController validates create/update/search
// requests against. Kept as a real enum so the allowed values live in exactly one place
// (the compiler enforces it) and show up in the OpenAPI document/generated frontend
// types — see DUP-12. `ClimbingRoute.Orientation` itself stays a plain nullable string
// column (see backend/CLAUDE.md: "free-form-ish string fields, not a fixed route
// 'type'"): this enum exists at the API/DTO boundary only, so no EF migration is needed.
[JsonConverter(typeof(LowercaseJsonStringEnumConverter<Orientation>))]
public enum Orientation
{
    North,
    Northeast,
    East,
    Southeast,
    South,
    Southwest,
    West,
    Northwest
}

// The fixed set of route gear types RoutesController validates create/update requests
// against — see Orientation above for the reasoning (DUP-12).
[JsonConverter(typeof(LowercaseJsonStringEnumConverter<Gear>))]
public enum Gear
{
    Bolted,
    Removable,
    Pitons,
    Aiders
}

// Shared by RoutesController and TicksController, both of which read a route's stored
// Orientation/Gear string (ClimbingRoute.Orientation/Gear — still a plain nullable string
// column, see Orientation above) into a response DTO typed with the enums above.
public static class RouteEnumParsing
{
    // Falls back to null rather than throwing on a value that doesn't parse — there's no
    // such row today (every write goes through RoutesController's
    // ValidateOrientation/ValidateGear), but a response shouldn't 500 if one ever shows up
    // (e.g. restored from an older backup) instead of just omitting the value.
    public static Orientation? ParseOrientation(string? value) =>
        value is not null && Enum.TryParse<Orientation>(value, ignoreCase: true, out var result) ? result : null;

    public static Gear? ParseGear(string? value) =>
        value is not null && Enum.TryParse<Gear>(value, ignoreCase: true, out var result) ? result : null;
}
