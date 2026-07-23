# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Acordanza is a small private web app for a group of friends to log and search climbing routes. Real accounts only (JWT auth, admin-provisioned) — no public sign-up, everyone who has an account can see and edit everything.

- `backend/Acordanza.Api/` — ASP.NET Core (.NET 10) Web API: EF Core + Npgsql, ASP.NET Core Identity, JWT auth. See `backend/CLAUDE.md`.
- `frontend/` — React 19 + TypeScript, built with Vite, package-managed with Bun. See `frontend/CLAUDE.md`.
- `docker-compose.yml` — local PostgreSQL container.

## Toolchain

Tool versions are pinned via `.prototools` (proto): Bun 1.3.14. .NET SDK is installed separately (net10.0 target).

## Running the whole stack locally

```
docker compose up -d                                              # start Postgres
cd backend/Acordanza.Api && dotnet ef database update && dotnet run   # API on :5073
cd frontend && bun install && bun dev                              # frontend on :5173
```

First-time backend setup also needs a JWT signing key (`dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"`) and at least one user account (`dotnet run -- create-user <username> <password>`, since there's no public registration endpoint). Full details are in `backend/CLAUDE.md` and the root `README.md`.

The `run-acordanza` skill (`.claude/skills/run-acordanza`) automates building, starting, and driving both processes end-to-end, including a headless-browser screenshot driver — use it instead of reinventing this when asked to run or verify the app.

## Architecture

Two independent processes talking over a JSON REST API (`VITE_API_URL`, default `http://localhost:5073/api`):

- The frontend holds no server state beyond a JWT + username in `localStorage` (`AuthContext`); every other piece of data (routes, ticks) is fetched fresh from the API per view.
- The backend is a thin CRUD layer over three EF Core entities — `ApplicationUser` (Identity), `ClimbingRoute`, `Tick` (a logged ascent of a route, many-to-one with both route and user) — exposed through `RoutesController` and `AuthController`. There is no business-logic layer between controllers and `AppDbContext`.
- Auth: `AuthController` issues JWTs on login (`JwtTokenService`); the frontend attaches them as `Authorization: Bearer` on every request via `frontend/src/api/client.ts`. Accounts are created out-of-band via the `create-user` CLI command in `Program.cs`, never via HTTP.

## Language

All user-facing text — frontend UI (labels, buttons, placeholders, empty states, `confirm()`/error messages) and any string that can surface in the frontend (e.g. backend `{ error }` payloads returned to the client) — must be in Spanish. Code, comments, identifiers, commit messages, and CLAUDE.md docs stay in English. `frontend/src/api/types.ts` has a `routeTypeLabels` map for translating the backend's English `type` enum values (`sport`, `trad`, …) at display time; reuse it rather than hardcoding translations per component.

## Cross-cutting notes

- Deploying: backend needs any .NET 10 + Postgres host with `ConnectionStrings:Default`, `Jwt:Key`, `Jwt:Issuer`/`Jwt:Audience`, `Cors:AllowedOrigins` set via environment variables; frontend is a static build (`bun run build` → `frontend/dist`) with `VITE_API_URL` baked in at build time. See root `README.md` for details. `infra/` holds a Terraform-managed Azure Container Apps deployment plus the GitHub Actions pipeline (`.github/workflows/backend-ci-cd.yml`) that builds, tests, and deploys the backend on push to `main` — see `infra/README.md`.
- No test suite exists yet in either project.
