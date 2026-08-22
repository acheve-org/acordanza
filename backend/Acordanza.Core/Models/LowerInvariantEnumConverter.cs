using System.Text.Json;
using System.Text.Json.Serialization;

namespace Acordanza.Core.Models;

// Every enum DTOs expose over the API (ReviewStatus, LinkSource, RouteImportKind,
// RouteImportStatus) used to be hand-serialized per call site as
// `X.ToString().ToLowerInvariant()` (DUP-7, ~18 call sites across 7 controllers). Every
// affected enum's members are single words (Pending, Approved, Human, Enrichment, ...),
// so there are no casing boundaries to split on — a plain invariant lowercase of the
// member name reproduces the exact same wire values `JsonNamingPolicy.CamelCase`/
// `SnakeCaseLower`/etc. would not (those only change behavior on multi-word names).
// There's no built-in "just lowercase it" policy in System.Text.Json, hence this.
//
// Applied via a type-level [JsonConverter] attribute on each of the four enums below,
// not via a JsonStringEnumConverter registered globally on JsonSerializerOptions in
// Acordanza.Api's Program.cs: a type-level attribute is honored by *any* serializer that
// touches the type — controller responses, the OpenAPI schema generator, and a plain
// `HttpClient.GetFromJsonAsync<T>()` with no options at all, which is what the API's own
// integration tests use — whereas an options-level registration would only cover
// whichever JsonSerializerOptions instance it was added to. Scoping it to just these four
// enums (rather than a blanket `JsonStringEnumConverter` converter factory) also matters:
// PasskeysController's WebAuthn DTOs carry several Fido2NetLib enums of their own
// (AttestationConveyancePreference, AuthenticatorAttachment, ...) that already serialize
// to the exact, sometimes-hyphenated strings the WebAuthn spec requires — a blanket
// factory registered in JsonSerializerOptions.Converters would take priority over their
// own attributes (that's System.Text.Json's converter-resolution order) and silently
// corrupt them.
public sealed class LowerInvariantEnumConverter<TEnum> : JsonStringEnumConverter<TEnum> where TEnum : struct, Enum
{
    public LowerInvariantEnumConverter() : base(LowerInvariantNamingPolicy.Instance)
    {
    }
}

public sealed class LowerInvariantNamingPolicy : JsonNamingPolicy
{
    public static readonly LowerInvariantNamingPolicy Instance = new();

    public override string ConvertName(string name) => name.ToLowerInvariant();
}
