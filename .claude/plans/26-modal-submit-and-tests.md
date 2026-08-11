# Cover the form modals with tests, then extract their submit boilerplate

Status: done
Depends on: [24](24-shared-presentation-helpers.md) for `errorMessage()` — the hook in §3 wraps exactly the expression 24 standardises, so landing 24 first makes this a smaller diff. Otherwise nothing. Frontend only.

## Problem

**Nine form modals, one body, written nine times.** `AddTickModal`, `AreaNameModal`, `AreaPolygonModal`, `ChangeEmailModal`, `FirstAscensionistNameModal`, `InviteUserModal`, `RejectReasonModal`, `RequestAreaExpertModal`, `RequestFirstAscensionistLinkModal` each carry:

```tsx
const [error, setError] = useState('');
const [submitting, setSubmitting] = useState(false);

async function handleSubmit(e: FormEvent) {
  e.preventDefault();
  setError('');
  setSubmitting(true);
  try {
    await someApiCall(…);
    onDone();
  } catch (err) {
    setError(err instanceof ApiError ? err.message : 'No se pudo …');
  } finally {
    setSubmitting(false);
  }
}
```

followed by the same `<p className="error-text">{error}</p>` and the same `ModalActions` pair (ghost "Cancelar", submit disabled while `submitting` with a pending label). It is ~15 lines per modal of pure ceremony around the two lines that differ.

The copies have already drifted in ways that matter:

- **`RejectReasonModal` clears `submitting` only in the `catch`**, not in a `finally` — deliberate (the modal unmounts on success, so resetting state would warn), but it means the one modal that diverges is the one where the divergence is invisible until you read all nine.
- **`AreaPolygonModal` calls the flag `saving`**, everyone else `submitting`.
- The fallback-message expression is inconsistent in exactly the ways plan [24](24-shared-presentation-helpers.md) catalogues.

**And 12 of the 13 modals have no test at all.** Of everything under `src/`, 25 files lack a `.test.tsx`; the pages among them are mostly thin, but the modals are not — they are where submit logic, error surfacing, disabled-state rules and the `allowRoleSelection`-style permission variations live. `DetailModal` is the only one covered. This is the single largest gap against the repo's own working agreement:

> **New page or component** → at minimum, it renders with realistic contract-typed data, and any permission-gated action is covered both ways.

The two halves are one plan because the ordering matters: refactoring nine untested modals onto a shared hook is exactly the change that should be done with tests already in place, not one that should produce them afterwards.

## Design

### 1. Tests first, against the current code

Before any refactor, each modal gets a `X.test.tsx` next to it, using the existing harness (`renderWithProviders`, MSW `server.use(...)`, `factories.ts`). Per modal, three cases — the shape the working agreement asks for, no more:

1. **Renders and submits**: fill the fields, submit, assert the request body the API received and that the success callback (`onInvited`/`onRequested`/`onClose`) fired.
2. **Surfaces a failure**: handler returns 400 with `{ error: "…" }`, assert that Spanish text renders and the success callback did **not** fire.
3. **The modal's own rule**: whatever is actually conditional in it — `InviteUserModal`'s `allowRoleSelection` (role field present and absent, covered both ways), `RejectReasonModal`'s submit disabled until a reason is typed, `RequestAreaExpertModal`'s exclusion of `excludedAreaIds` from the picker and its "No quedan sectores disponibles" state, `AddTickModal`'s optional rating, `FirstAscensionistNameModal`/`AreaNameModal`'s create-vs-rename fallback copy.

Plus, for the two modals that fetch (`RequestAreaExpertModal`, `RequestFirstAscensionistLinkModal`): a case pinning that they fetch **on open, themselves** — which MSW's `onUnhandledRequest: 'error'` already enforces from the other direction, but which deserves a positive assertion in the file that owns the behaviour.

**`AreaPolygonModal` is excluded from the test half.** It renders Leaflet, and `frontend/CLAUDE.md` puts map rendering out of scope for the jsdom layer ("testing it here means mocking so much the test ends up asserting the mocks"). It still gets the §3 refactor, verified through `run-acordanza`.

### 2. Then the hook

New `src/hooks/useSubmit.ts`:

```ts
// The shape every form modal shares: clear the error, disable the submit while
// in flight, turn a thrown ApiError into Spanish copy, and only run onSuccess
// when the call actually succeeded.
export function useSubmit(fallbackMessage: string) {
  const [error, setError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  async function submit(action: () => Promise<unknown>, onSuccess?: () => void) {
    setError('');
    setSubmitting(true);
    try {
      await action();
      onSuccess?.();
    } catch (err) {
      setError(errorMessage(err, fallbackMessage));
      setSubmitting(false);   // only on failure — see below
    }
  }

  return { error, submitting, submit, setError };
}
```

**No `finally`.** This adopts `RejectReasonModal`'s behaviour as the rule rather than the exception: on success the modal unmounts (or its parent closes it), so clearing `submitting` afterwards is at best pointless and at worst a state update on an unmounted component. Standardising on the one variant that thought about it is the point of extracting at all.

**A hook, not a `FormModal` component.** A wrapper component would have to own the `<form>`, the error paragraph, and the action buttons — and the nine modals differ in exactly those places (`RejectReasonModal` needs `autoFocus` on a `textarea`, `AddTickModal` has a `.grid-3` field row, `AreaPolygonModal`'s actions sit under a map, several have distinct pending labels: "Enviando…", "Registrando…", "Solicitando…", "Rechazando…"). A component would grow a prop per difference and end up harder to read than the markup it replaced. The hook takes the part that is genuinely identical — the state machine — and leaves the markup explicit.

**Not in `design-system/`.** It depends on `ApiError` and `errorMessage`, which are app concerns; the design system knows nothing about this app's API. `Modal`/`ModalActions`/`Button` stay where they are.

### 3. Applying it

Each modal loses its two `useState`s and its `handleSubmit` body:

```tsx
const { error, submitting, submit } = useSubmit('No se pudo enviar la invitación');

async function handleSubmit(e: FormEvent) {
  e.preventDefault();
  await submit(() => usersApi.inviteUser({ email: email.trim(), role }), onInvited);
}
```

Everything else — the markup, the field state, the disabled conditions, the pending labels — is untouched. `AreaPolygonModal` renames `saving` → `submitting` as part of this. `FirstAscensionistNameModal` and `AreaNameModal` pass their create-vs-rename ternary as the `fallbackMessage` argument.

The guard clauses that currently sit *before* `setSubmitting(true)` (`RejectReasonModal`'s `if (!reason.trim()) return;`, `RequestAreaExpertModal`'s `if (!areaId) return;`) stay in `handleSubmit`, before the `submit(...)` call — they are per-modal validation, not part of the shared machine.

The `setError` returned by the hook is for the two modals that surface a *non-submit* error (`RequestAreaExpertModal`'s failed options fetch); once [25](25-finish-react-query-migration.md)'s slice G converts those fetches to queries, that error comes from the query instead and `setError` can leave the hook's return type.

## Steps

1. **`AddTickModal.test.tsx`, `InviteUserModal.test.tsx`, `RejectReasonModal.test.tsx`** — the three simplest, to settle the file shape.
2. **`AreaNameModal.test.tsx`, `FirstAscensionistNameModal.test.tsx`, `ChangeEmailModal.test.tsx`.**
3. **`RequestAreaExpertModal.test.tsx`, `RequestFirstAscensionistLinkModal.test.tsx`** — including the fetch-on-open assertions.
4. `src/hooks/useSubmit.ts` + `useSubmit.test.ts`.
5. Convert the nine modals, one commit per modal or one per group; the tests from 1–3 must pass **unchanged** through every conversion. That is the whole safety argument for the ordering — a green suite that was written against the old code is the only thing proving the hook preserved behaviour.
6. `bun run lint && bun run test && bun run build`.
7. `frontend/CLAUDE.md`: add `useSubmit` beside `useDisclosure` in the hooks sentence, with the no-`finally` reasoning stated once (it is the kind of deliberate omission someone will otherwise "fix").

Steps 1–3 are worth landing on their own even if 4–6 never happen: the coverage gap is the more valuable half.

## Testing

Covered by construction — this plan *is* mostly tests. Beyond them:

- **`useSubmit.test.ts`:** `submitting` is true during the call and false after a failure; `onSuccess` runs on success and not on failure; `error` carries the `ApiError` message and falls back to the given string for a non-`ApiError` throw; a second submit clears the previous error.
- **No new `run-acordanza` pass** except for `AreaPolygonModal` (§1): open `/manage/areas` → "Dibujar polígono", draw and save, and confirm the failure path still shows its message — the one modal the jsdom layer can't reach.
- `bun run test` must end at 32 files + the new ones, with every pre-existing test untouched.
