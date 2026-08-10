# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Acordanza is a small private web app for a group of friends to log and search climbing routes. Real accounts only (JWT auth — provisioned via CLI or an in-app email invite) — no public sign-up. Inviting is not admin-only: `user.invite` is in every role's permission set, capped by a backend-managed per-role quota (`Invites:MaxForUser`/`MaxForModerator`, admins unbounded — issue #113). Every account can read everything; write access is role-based (`admin`/`moderator`/`user`, see `backend/CLAUDE.md`), plus two dynamic grants: users linked to a `FirstAscensionist` record via an approved claim, and approved `AreaExpert`s (route-validation rights scoped to their area).

- `backend/Acordanza.Api/` — ASP.NET Core (.NET 10) Web API: EF Core + Npgsql, ASP.NET Core Identity, JWT auth. See `backend/CLAUDE.md`.
- `frontend/` — React 19 + TypeScript, built with Vite, package-managed with Bun. See `frontend/CLAUDE.md`.
- `design-system/` — `acordanza-design-system`, a sibling Bun workspace package (`frontend`'s `workspace:*` dependency) holding the shared component set and design tokens. `frontend/predev`/`prebuild` build it first. See `/DESIGN.md` for the token/component spec.
- `landing/` — a standalone, zero-build static HTML marketing page (not part of the Bun workspace, no framework); deployed independently of `frontend`/`design-system` (see `deploy-landing` in `.github/workflows/ci-cd.yml`). Keeps its own copy of the design-system's color/typography tokens inline, since it has no build step to import them — see the comment at the top of `landing/index.html`.
- `docker-compose.yml` — local Postgres container plus an Azurite container (Azure Blob Storage emulator, for topo images and Data Protection keys). `docker-compose.override.yml` additionally runs the backend itself in a `dotnet watch` container — see root `README.md`.

## Toolchain

Tool versions are pinned via `.prototools` (proto): Bun 1.3.14, Node 26.6.0, Terraform 1.15.8 — CI reads the Bun and Terraform versions straight out of this file, so bump it there, not in the workflow. .NET SDK is installed separately (net10.0 target).

## Running the whole stack locally

```
docker compose up -d                                              # start Postgres + Azurite
cd backend/Acordanza.Api && dotnet ef database update && dotnet run   # API on :5073
cd frontend && bun install && bun dev                              # frontend on :5173
```

First-time backend setup also needs a JWT signing key (`dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"`) and at least one user account (`dotnet run -- create-user <email> <password> <name> [role]`, since there's no public registration endpoint). No email provider setup is needed locally — without a `Resend:ApiKey`, the backend logs emails (invites, password resets) to the console instead of sending them. Full details are in `backend/CLAUDE.md` and the root `README.md`.

The `run-acordanza` skill (`.claude/skills/run-acordanza`) automates building, starting, and driving both processes end-to-end, including a headless-browser screenshot driver — use it instead of reinventing this when asked to run or verify the app.

## Architecture

Two independent processes talking over a JSON REST API (`VITE_API_URL`, default `http://localhost:5073/api`):

- The frontend holds no server state beyond a JWT + username in `localStorage` (`AuthContext`); every other piece of data (routes, ticks, areas, passkeys, ...) is fetched fresh from the API per view.
- The backend is a thin CRUD layer over EF Core entities (`ApplicationUser`, `ClimbingRoute`, `Tick`, `Area`, `FirstAscensionist`, `FirstAscensionistLinkRequest`, `AreaExpert`, `Topo`, `PasskeyCredential`) exposed through one controller per resource — see `backend/CLAUDE.md` for the full list. There is no business-logic layer between controllers and `AppDbContext`.
- Auth: `AuthController` issues JWTs on login (`JwtTokenService`); the frontend attaches them as `Authorization: Bearer` on every request via `frontend/src/api/client.ts`. Login is optionally two-step — a user with a registered passkey must also complete a WebAuthn assertion (`POST /api/auth/login/passkey`) before a JWT is issued. Accounts are provisioned either out-of-band via the `create-user` CLI command in `Program.cs`, or in-app by any user holding `user.invite` (`UsersController.InviteUser`, quota-capped per role) via an emailed single-use link to `/accept-invite` — there is still no public self-registration endpoint. Each account has one role (`admin`/`moderator`/`user`); the role's permissions (see `backend/Acordanza.Api/Authorization/`) plus any dynamic permissions (see `DynamicPermissions` — granted to users linked to a `FirstAscensionist` record) are embedded as JWT claims and enforced via `[Authorize(Policy = ...)]` on write endpoints, and returned from `GET /auth/me` so the frontend (`AuthContext.hasPermission`) can hide actions the current user can't perform. Approved `AreaExpert`s get an additional, ownership-scoped route-validation right checked per-request (`AreaExpertAuthorizer`) rather than via a claim — see backend `CLAUDE.md`.
- Validation gates list visibility outside "Mi acordanza" (issue #72): a route/area/first-ascensionist is visible in browse lists (`/routes`, `/explore`, `/fa`, pickers, ...) only if it's `Approved` **or** was added by the caller — `visible(x) ⟺ x.ValidationStatus == Approved || x.AddedByUserId == callerId` (`Models/ValidationVisibility.cs`). Detail-by-id/slug reads and "Mi acordanza" pages (own ticks, own openings, own contributions) are exempt — see backend `CLAUDE.md`.

## Language

All user-facing text — frontend UI (labels, buttons, placeholders, empty states, `confirm()`/error messages) and any string that can surface in the frontend (e.g. backend `{ error }` payloads returned to the client) — must be in Spanish. Code, comments, identifiers, commit messages, and CLAUDE.md docs stay in English. `frontend/src/api/types.ts` has `orientationLabels`/`gearLabels`/`roleLabels` maps for translating the backend's English enum values at display time; reuse them rather than hardcoding translations per component.

Route slugs (the `path`/`to` values in `frontend/src/App.tsx`'s `<Route>`/`<Link>`/`<NavLink>` elements) count as identifiers, not user-facing text — keep them in English (`/routes`, `/explore`, `/fa`, `/routes/new`, `/my-acordanza/my-areas`, `/my-acordanza/claim-first-ascensionist`, `/my-acordanza/contributions`, `/my-acordanza/my-routes`, `/my-acordanza/my-openings`, `/my-acordanza/my-invites`, `/manage/areas`, `/manage/first-ascensionists`, `/manage/users`, `/manage/pending-tasks`) even though the label rendered inside the link stays Spanish (`Busca`, `Explora`, `Sectores`, ...). Note `/my-acordanza/my-routes` ("Mis vías") is the current user's tick history, not the routes they opened — that's `/my-acordanza/my-openings` ("Mis aperturas"). Both `/my-acordanza` (`pages/MyAcordanzaLayout.tsx`) and `/manage` (`pages/ManageLayout.tsx`, reached from the top nav's "Gestionar" item, shown only if the user can edit areas or manage users) are left-nav shells over nested pages — see `frontend/CLAUDE.md`.

The app is organized around four sections (issue #108), which are the top nav's items, the home page's four cards, and one dedicated page each — keep the three in sync when adding or renaming one:

| Section | Path | Page | What it's for |
|---|---|---|---|
| Busca | `/routes` | `pages/SearchPage.tsx` | the routes list + filters (name, sector, grade, orientation, gear, topo) |
| Explora | `/explore` | `pages/ExplorePage.tsx` | the map plus a browsable list of zones, leading into `/areas/*` |
| Sigue | `/fa` | `pages/FirstAscensionistsDirectoryPage.tsx` (+ `FirstAscensionistPage.tsx` per opener) | discovering aperturistas and what they've opened |
| Mi acordanza | `/my-acordanza` | `pages/MyAcordanzaLayout.tsx` | the user's own ticks, invites, areas, claims |

`/` is the home page (`pages/HomePage.tsx`), a hub over those four — not a content page of its own.

## Cross-cutting notes

- Deploying: backend needs any .NET 10 + Postgres host with `ConnectionStrings:Default`, `Jwt:Key`, `Jwt:Issuer`/`Jwt:Audience`, `Cors:AllowedOrigins`, `Frontend:BaseUrl`, an Azure Blob Storage account (`Storage:*`, for topo images and Data Protection key persistence — see backend `CLAUDE.md`), and ideally `Resend:ApiKey` (transactional email; falls back to console logging without one) set via environment variables; frontend is a static build (`bun run build` → `frontend/dist`, which builds `design-system/` first) with `VITE_API_URL` baked in at build time. See root `README.md` for details. `infra/` holds a Terraform-managed Azure Container Apps deployment plus the GitHub Actions pipeline (`.github/workflows/ci-cd.yml`) that builds/tests backend, frontend, and `landing/` and deploys all three (backend and frontend as Container Apps, `landing/` as a separate Azure Static Web App) on push to `main` — see `infra/README.md`. Two further jobs gate every push independently of the build: `openapi-contract-check` (regenerates the OpenAPI document and the frontend's `generated.ts`, fails on any diff — see backend/frontend `CLAUDE.md`) and `security-scan` (Trivy over IaC/Dockerfiles and for leaked secrets, zizmor over the workflows themselves; `.trivyignore` holds the accepted findings).
- Testing: `backend/Acordanza.Api.Tests/` (`dotnet test Acordanza.slnx` from the repo root — see `backend/CLAUDE.md`) holds both fast pure-unit tests and Docker-dependent integration tests (real Postgres + Azurite via Testcontainers, real HTTP through `WebApplicationFactory`), the latter gated behind a `Category=Integration` trait; frontend tests run via `bun run test` in `frontend/` (Vitest + Testing Library + MSW — pure modules plus rendering/page tests against a mocked API, see `frontend/CLAUDE.md`'s Testing section) and, separately, in `design-system/` (its own Vitest setup, component-level keyboard/focus behaviour — see `/DESIGN.md`). All three are wired into CI (`.github/workflows/ci-cd.yml`). No real-browser frontend integration or E2E tests in this suite — jsdom-rendered component tests cover that layer instead; a handful of true E2E smoke journeys, if ever wanted, belong in the `run-acordanza` skill (Playwright, deliberately tiny) rather than here.
- Planning: non-trivial work is planned in-repo before it's built — one file per change at `.claude/plans/NN-slug.md` (checked in, numbered, headed by `Status:` / `Issue:` / `Depends on:` lines), landed on its own via a `plan: <title>` commit, then implemented in a follow-up PR referencing the same issue. Follow the existing files' shape when adding one.
