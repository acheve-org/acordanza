# Extract the approval-workflow primitive (not the controllers)

Status: planned (not started) — deliberately deferred until a third flow appears or these controllers are next touched
Depends on: nothing

## Problem

`AreaExpertsController` and `FirstAscensionistLinksController` are structural clones: `GET /mine`, `GET /pending`, `POST` (request, with AnyAsync pre-check + `DbUpdateException` catch against the unique filtered pending index), `POST /{id}/approve`, `POST /{id}/reject`, `DELETE /{id}` (self-service cancel), a private `Query()` with the same three Includes, `ToDto`, and `NotifyDecisionAsync` (best-effort email, try/catch + log). Their models (`AreaExpert`, `FirstAscensionistLinkRequest`) are near-identical shapes with separate but identical status enums (`AreaExpertStatus`, `FirstAscensionistLinkStatus`).

## What NOT to do

Do not build a generic `ApprovalController<T>` or merge the two controllers. `FirstAscensionistLinksController.Approve` has a genuinely different shape: a transaction wrapping a second conditional UPDATE (claim the `FirstAscensionist.UserId` only if still unlinked) with an auto-reject-on-conflict fallback. A generic base would contort around it. The controllers stay; only the repeated primitive moves.

## Design

Extract the smallest shared pieces:

1. **One shared status enum.** Replace `AreaExpertStatus` and `FirstAscensionistLinkStatus` with a single `ReviewStatus { Pending, Approved, Rejected }` in `Models/`. Check `AppDbContext`/migrations first for how the enums are stored (string conversion vs int) — if stored as strings the rename is data-compatible and needs only a no-op-ish migration; if the property types change, `dotnet ef migrations add` and inspect the generated migration carefully (the unique filtered indexes on `Status = 'Pending'` reference the stored value).

2. **`IReviewableRequest` interface** on both entities: `int Id`, `ReviewStatus Status`, `DateTimeOffset? DecidedAt`, `string? DecidedByUserId`, `string? RejectionReason`, `DateTimeOffset RequestedAt`, `string UserId`.

3. **`Services/ReviewRequestService`** (or a static helper — it only needs a `DbSet<T>`):
   ```csharp
   public enum DecisionOutcome { Applied, AlreadyResolved, NotFound }
   Task<DecisionOutcome> ApproveAsync<T>(DbSet<T> set, int id, string deciderId) where T : class, IReviewableRequest;
   Task<DecisionOutcome> RejectAsync<T>(DbSet<T> set, int id, string deciderId, string reason) where T : ...;
   Task<DecisionOutcome> CancelAsync<T>(DbSet<T> set, int id, string ownerUserId) where T : ...;   // ExecuteDelete scoped to owner+Pending
   ```
   Each wraps the existing atomic `ExecuteUpdateAsync`/`ExecuteDeleteAsync` conditioned on `Status == Pending` plus the "0 rows → exists? AlreadyResolved : NotFound" disambiguation. Verify EF can translate `ExecuteUpdateAsync` setters through the interface-constrained generic (it can via `e => e.Status` when T is constrained to the interface and the property is mapped; if translation fails, fall back to passing setter expressions from the caller).

4. **Best-effort notifier**: a tiny `Notifications.BestEffortAsync(ILogger, Func<Task>)` helper (try/catch + LogWarning) replacing the two hand-rolled try/catch bodies; the email-template choice stays per-controller.

`FirstAscensionistLinksController.Approve` keeps its transaction + second UPDATE + auto-reject inline, but expresses the two status transitions through the service.

## Steps

1. Unify the enums (incl. migration check) — smallest reviewable commit on its own.
2. Add `IReviewableRequest` + service; port `AreaExpertsController` (the simple twin) first.
3. Port `FirstAscensionistLinksController`, keeping its extra linking logic inline.
4. `dotnet build`; exercise approve/reject/cancel through the UI (`/manage/pending-tasks`, `/my-acordanza/my-areas`, `/my-acordanza/claim-first-ascensionist`); confirm double-approve still returns "ya se ha resuelto esta solicitud".
5. Update `backend/CLAUDE.md` (Models bullet mentions the twin shapes; Controllers bullet).

## Trigger

Route/area *validation* is already a third pending-review-ish flow feeding `PendingTasksContext`, but it has no request row — don't force it in. Execute this plan when (a) a real fourth request/approve/reject flow appears, or (b) a bug fix has to touch both twin controllers at once.
