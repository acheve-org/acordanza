using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace Acordanza.Api.Controllers;

// Every write endpoint that scopes a query/write to "the caller" used to re-derive the
// same id by hand — `userManager.GetUserId(User)!` — at each call site (DUP-7, ~50
// call sites across 10 controllers). UserManager<TUser>.GetUserId(ClaimsPrincipal) is
// itself nothing but `principal.FindFirstValue(UserIdClaimType)`, where the default
// UserIdClaimType is ClaimTypes.NameIdentifier — a pure claims lookup, no DB access —
// so centralizing it here needs no UserManager dependency at all. JwtTokenService
// issues the user id as the standard `sub` claim, which the JWT bearer handler's
// default inbound claim map turns into ClaimTypes.NameIdentifier before authorization
// runs, so this reads the identical claim every prior call site did.
//
// The `!` preserves the same non-null assumption every original call site made: every
// action on a controller deriving from this one requires [Authorize], so an
// authenticated request always carries this claim.
public abstract class ApiControllerBase : ControllerBase
{
    protected string CallerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
