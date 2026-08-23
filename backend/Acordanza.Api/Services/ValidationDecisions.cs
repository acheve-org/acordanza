using Acordanza.Api.Dtos;
using Acordanza.Core.Data;
using Acordanza.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Acordanza.Api.Services;

// The load/authorize/transition/save plumbing shared by every entity's validate/reject
// action pair (Area, ClimbingRoute, FirstAscensionist — all IValidatable, sharing the
// Validation.MarkValidated/MarkRejected state transition). Callers own the not-found
// message text and any entity-scoped authorization beyond the action's own
// [Authorize(Policy = ...)] attribute — routes need one, since an area expert can
// validate/reject within their area even without the route.validate role claim (see
// AreaExpertAuthorizer) — passed in as a predicate over the loaded entity. Returns null
// on success, or the exact ActionResult the caller should return on failure, so each
// controller action collapses to "return failure ?? Ok(...)".
public static class ValidationDecisions
{
    public static async Task<ActionResult?> ValidateAsync<T>(
        AppDbContext db, DbSet<T> set, int id, string notFoundMessage, string userId,
        Func<T, Task<bool>>? authorize = null)
        where T : class, IValidatable
    {
        var (entity, failure) = await LoadAsync(set, id, notFoundMessage, authorize);
        if (failure is not null) return failure;

        Validation.MarkValidated(entity!, userId);
        await db.SaveChangesAsync();
        return null;
    }

    public static async Task<ActionResult?> RejectAsync<T>(
        AppDbContext db, DbSet<T> set, int id, string notFoundMessage, string userId, string reason,
        Func<T, Task<bool>>? authorize = null)
        where T : class, IValidatable
    {
        var (entity, failure) = await LoadAsync(set, id, notFoundMessage, authorize);
        if (failure is not null) return failure;

        Validation.MarkRejected(entity!, userId, reason);
        await db.SaveChangesAsync();
        return null;
    }

    private static async Task<(T? Entity, ActionResult? Failure)> LoadAsync<T>(
        DbSet<T> set, int id, string notFoundMessage, Func<T, Task<bool>>? authorize)
        where T : class, IValidatable
    {
        var entity = await set.FindAsync(id);
        if (entity is null) return (null, new NotFoundObjectResult(new ApiError(notFoundMessage)));

        if (authorize is not null && !await authorize(entity))
        {
            return (null, new ForbidResult());
        }

        return (entity, null);
    }
}
