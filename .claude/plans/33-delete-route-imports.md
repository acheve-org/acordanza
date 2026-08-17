# Delete a `RouteImport` row from "Mis importaciones"

Status: proposed
Depends on: [30](30-route-from-topo.md) and [31](31-route-from-url.md), whose `RouteImport` table, `RouteImportsController` and `MyImportsPage` this extends. No migration — no schema change, only a new endpoint and a new frontend affordance.

## Problem

"Volver a intentarlo" on a `Discarded` row never re-enqueues — it just repopulates the form (`MyImportsPage.tsx:73-91`), so a resubmission creates a brand-new `RouteImport` row and the old one just sits there. Same for a `Failed` row, which has no retry affordance at all today. There is no way to remove either kind of row, so a user who iterates on a bad photo or a mistyped URL a few times accumulates permanent clutter in their own list, and there is no `DELETE` endpoint on `RouteImportsController` at all today.

This is scoped to tidying up that clutter — removing an import *record* the user doesn't want to look at anymore. It is explicitly not an "undo": deleting a `RouteImport` row never touches the `ClimbingRoute`/`Topo`/`Link` rows it may have produced (`RouteImport.Route` is already `DeleteBehavior.SetNull`, per `AppDbContext.cs:250-255` — the schema already assumes an import can vanish without taking its route down with it). Undoing a bad machine-created route is what `/manage/pending-tasks` and the route's own delete/edit are for.

## Design

### 1. Which rows can be deleted

Only `Discarded` and `Failed` — the two states that produced nothing. `Pending` is refused because the worker may be mid-flight (a queued message referencing this `ImportId`, possibly already dequeued and spending against a real Foundry call) and deleting the row out from under it would race an in-progress write. `Completed` is refused because the row is the only record of *how* a route or a topo/link attachment came to exist — `RouteImport.Route`'s `SetNull` behaviour already protects the route from a `RouteImport` delete, but deleting the provenance on purpose while the route survives would just be confusing, not useful, and nothing in this plan needs it.

### 2. `DELETE /api/route-imports/{id}`

New action on `RouteImportsController`, class-level `[Authorize]` only (no new permission — same shape as `GET /mine`), ownership-scoped inline like `TicksController.DeleteTick`/`PasskeysController.Delete`:

```csharp
[HttpDelete("{id:int}")]
public async Task<ActionResult> DeleteImport(int id)
{
    var userId = userManager.GetUserId(User)!;
    var import = await db.RouteImports.FirstOrDefaultAsync(i => i.Id == id && i.SubmittedByUserId == userId);
    if (import is null) return NotFound();

    if (import.Status is RouteImportStatus.Pending or RouteImportStatus.Completed)
    {
        return Conflict(new { error = "No se puede eliminar una importación pendiente o completada" });
    }

    if (import.BlobName is { } blobName) await blobStorage.DeleteAsync(blobName);

    db.RouteImports.Remove(import);
    await db.SaveChangesAsync();
    return NoContent();
}
```

Unlike `TicksController`'s silent no-op, a not-found/not-owned row is a plain `404` and a wrong-status row is a `409` — the frontend only ever shows the delete button on `Discarded`/`Failed` rows (§4), so either response should only ever surface from a stale UI or a double-click race, never a real user decision; disambiguating them costs nothing and matches `ReviewRequestService`'s "say which reason" precedent rather than `TicksController`'s "ownership mismatch, nothing to distinguish" one.

`blobStorage.DeleteAsync` is `DeleteIfExistsAsync` under the hood (`BlobStorageService.cs:86-89`), so calling it unconditionally on `BlobName` is safe for both cases it can be null/stale for: a `Discarded` Image row's blob is already gone (`RouteImportService.DiscardAsync`, `RouteImportService.cs:398-402`, deletes it but never clears the field) and a Url row's `BlobName` is always null. Only a `Failed` Image row has a real blob still under `_imports/` to clean up — deleting it here beats waiting up to 7 days for the lifecycle sweep.

No rate limit — this acts only on the caller's own already-existing rows, unlike the two `POST` endpoints, which spend money and make outbound calls.

### 3. Frontend: `deleteRouteImport`

`api/routeImports.ts`:

```ts
export function deleteRouteImport(id: number): Promise<void> {
  return api.delete<void>(`/route-imports/${id}`);
}
```

### 4. `MyImportsPage.tsx`

A `deleteMutation` (`useMutation`, invalidating `queryKeys.myRouteImports()` on success — same prefix-invalidate pattern every other mutation on this page uses) backing a new trailing "Eliminar" ghost button, shown only when `item.status === 'discarded' || item.status === 'failed'` — i.e. exactly the rows the backend will accept, so a normal user never sees the button produce a `409`. Alongside "Volver a intentarlo" on `Discarded` rows, not replacing it: retry and delete are different intents (§Problem), and this plan deliberately leaves retry's behaviour (create-new-row, old row untouched) alone.

Confirm before calling the API — `¿Eliminar esta importación?` — matching the `confirm()` pattern the root `CLAUDE.md` notes elsewhere in the frontend; this is irreversible (the row and its `Outcome` reason are gone for good).

```tsx
{(item.status === 'discarded' || item.status === 'failed') && (
  <Button variant="ghost" small type="button" onClick={() => handleDelete(item)}>
    Eliminar
  </Button>
)}
```

## Not doing

- **Deleting/unlinking the created route** when a `Completed` row is removed — out of scope, see §Problem. `Completed` rows aren't deletable at all in this plan.
- **Auto-deleting the old row on retry.** Settled during scoping: keeps the two actions independent and avoids a partial-failure state (new submission fails after the old row is already gone). A cheap follow-up if clutter-after-retry is still annoying once delete exists.
- **A moderator/admin override.** Owner-only, matching every other self-service delete in this codebase (`TicksController`, `PasskeysController`, `AreaExpertsController.Cancel`). No stated need for anyone else to clean up another user's imports.
- **Rate-limiting the new endpoint.** It only ever removes the caller's own already-existing rows — no spend, no outbound call — unlike `POST /route-imports/{image,url}`.

## Files touched

| File | Change |
| --- | --- |
| `backend/Acordanza.Api/Controllers/RouteImportsController.cs` | + `DELETE /{id}` (§2) |
| `backend/Acordanza.Api.Tests/Controllers/RouteImportTests.cs` | new cases (§Testing) |
| `frontend/src/api/routeImports.ts` | + `deleteRouteImport` (§3) |
| `frontend/src/pages/MyImportsPage.tsx` | delete mutation + button + confirm (§4) |
| `frontend/src/pages/MyImportsPage.test.tsx` | new cases (§Testing) |
| `backend/CLAUDE.md`, `CLAUDE.md` | note the new endpoint under Route import |

## Testing

**API** (`Acordanza.Api.Tests/Controllers/RouteImportTests.cs`, `Category=Integration`):

- `DELETE` on a `Discarded` row owned by the caller: `204`, row gone from the database.
- `DELETE` on a `Failed` Image row: `204`, and the staged blob is gone (assert against Azurite, same style the existing promote/discard tests use).
- `DELETE` on a `Pending` row: `409`, row still present.
- `DELETE` on a `Completed` row: `409`, row still present.
- `DELETE` on another user's row, and on a nonexistent id: both `404`.

**Frontend** (`MyImportsPage.test.tsx`, Vitest + MSW): the "Eliminar" button renders for `discarded`/`failed` rows and not for `pending`/`completed`; clicking it and confirming calls the `DELETE` endpoint and the row disappears after refetch; declining the `confirm()` makes no request (mock `window.confirm`, same as any other destructive-action test in this suite).

`dotnet test Acordanza.slnx` from the repo root with Docker up; `bun run test` in `frontend/`. No OpenAPI regen needed beyond the new operation itself — `DELETE /route-imports/{id}` returning `204`/`void` needs no new DTO, but `openapi-contract-check` still catches it if `generated.ts` isn't re-run (`bun run generate-api`).
