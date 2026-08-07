# Collapse the twin pending-request components

Status: planned (not started) — lowest priority of the set; do opportunistically when touching either file
Depends on: nothing (if frontend-data-fetching.md lands first, write the merged component with mutations from the start)

## Problem

`components/PendingAreaExpertRequests.tsx` and `components/PendingFirstAscensionistLinkRequests.tsx` are 73-line near-identical twins. Differences, in full:

1. The API module called for approve/reject (`areaExperts` vs `firstAscensionistLinks`).
2. The second field row: label "Sector" + `e.areaName` vs label "Aperturista" + `r.firstAscensionistName`.
3. The item type (`AreaExpert` vs `FirstAscensionistLinkRequestSummary`).

Everything else — empty state, `List`/`ListRow` layout, the "Usuario" field, the `Solicitado el {date}` subtitle, Aprobar/Rechazar buttons, `RejectReasonModal` wiring with the same title format — is identical.

## Design

One generic component, e.g. `components/PendingRequestsList.tsx`:

```tsx
interface PendingRequestItem {
  id: number;
  userName: string;
  requestedAt: string;
}

interface Props<T extends PendingRequestItem> {
  items: T[];
  subjectLabel: string;                    // "Sector" | "Aperturista"
  subjectName: (item: T) => string;        // e => e.areaName / r => r.firstAscensionistName
  onApprove: (id: number) => Promise<void>;
  onReject: (id: number, reason: string) => Promise<void>;
  onChange: () => void;
}
```

The two existing components become thin wrappers (or disappear entirely, with `PendingTasksPage` passing the props inline — prefer thin wrappers so the page stays readable and the API imports stay near their use). Keep all user-facing strings Spanish and unchanged; keep the `tr-field`/`tr-field-label` markup so `styles.css` needs no edits.

## Steps

1. Add `PendingRequestsList.tsx` with the shape above.
2. Reduce both existing components to wrappers supplying the four differing props; check `PendingTasksPage.tsx` (their only consumer — verify with grep) still renders identically.
3. `bun run build && bun run lint`; visual check of `/manage/pending-tasks` with at least one pending request of each kind (create via `/my-acordanza/my-areas` and `/my-acordanza/claim-first-ascensionist` with a second account).
4. Update the `components/` bullet in `frontend/CLAUDE.md` (it names both components).

## Note

If a third request type ever appears (see approval-workflow-primitive.md), this component is where its review UI plugs in for free — that, more than the 60 saved lines, is the point.
