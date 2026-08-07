# Single-source the validation lifecycle (ValidatedAt / ValidatedByUserId)

Status: done
Depends on: nothing (smallest plan of the set; good warm-up task)

## Problem

Two entities carry the same validation lifecycle — `ClimbingRoute` and `Area` both have `ValidatedAt` / `ValidatedByUserId`, set by a validate endpoint and cleared when the record is edited — but the rule is re-implemented at each site and has already drifted:

- `AreasController.UpdateArea` and `UpdateAreaCoordinates` clear validation **only when something actually changed** (explicit `changed` computation), so re-saving a form as-is doesn't create a spurious pending-validation task.
- `RoutesController.UpdateRoute` clears validation **unconditionally** — re-saving a route form untouched silently un-validates it and re-adds it to every validator's pending list.
- `AreasController.CreateArea` auto-validates when the creator holds `area.validate`; `RoutesController.CreateRoute` has no equivalent auto-validation for `route.validate` holders (verify whether that asymmetry is intended before touching it — it may be a deliberate product decision, or just never considered).

## Design

1. `Models/IValidatable.cs`:
   ```csharp
   public interface IValidatable
   {
       DateTimeOffset? ValidatedAt { get; set; }
       string? ValidatedByUserId { get; set; }
   }
   ```
   Implemented by `ClimbingRoute` and `Area` (properties already exist; just add the interface).

2. A static helper (e.g. `Models/Validation.cs` or `Services/`), so the clear-only-if-changed rule has one home:
   ```csharp
   public static class Validation
   {
       public static void MarkValidated(IValidatable entity, string userId) { ... }   // sets both
       public static void InvalidateIfChanged(IValidatable entity, bool changed) { ... } // clears both when changed
   }
   ```

3. Adopt at all five sites: `RoutesController.UpdateRoute` + `ValidateRoute`, `AreasController.CreateArea` (auto-validate branch) + `UpdateArea` + `UpdateAreaCoordinates` + `ValidateArea`.

## The one real decision

Bringing `UpdateRoute` in line means computing `changed` for routes: compare `Name`, `AreaId`, `Grade`, `Orientation`, `Gear`, `Description`, `Tags`, **and the `FirstAscensionists` id set** (order-insensitive) against the request. This is a small behavior change (no-op saves stop invalidating) — it matches the documented intent of the area rule ("so re-saving the form as-is doesn't generate a spurious pending-validation task") and should be called out in the commit message. If the maintainer prefers current behavior, adopt the helper anyway and pass `changed: true` unconditionally for routes, keeping the drift visible in one line instead of implicit.

## Steps

1. Add interface + helper; implement on both entities. No migration needed (no schema change).
2. Replace the five inline sites; delete the per-site comments that re-explain the rule (the helper's doc comment carries it).
3. Decide/implement the `UpdateRoute` changed-computation (see above).
4. `dotnet build`; manual check: edit an area without changes → stays validated; edit a route without changes → per the decision above; validate route/area still works; `PendingTasksContext` counts behave.
5. Update `backend/CLAUDE.md` if wording about the invalidation rule changes.
