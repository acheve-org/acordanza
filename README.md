# Acordanza — a shared climbing route log

A small private web app for logging and searching climbing routes with a few friends.
ASP.NET Core (.NET 10) Web API + PostgreSQL backend, React + Vite frontend.

## Features
- Add routes: name, area, grade (French scale, optional) and artificial grade (A0–A5 / C0–C5), type (sport/trad/boulder/etc.), description, length, gear notes, links, first ascent
- Search & filter by name/description, area, and type
- Log ascents ("ticks") per route: who, when, rating, notes
- Real user accounts (JWT auth, email as username) — everyone who has an account can see and edit everything
- Accounts are admin-provisioned; there is no public sign-up
- Optional passkey (WebAuthn/FIDO2) second factor, and a settings page to manage it and change your password

## Architecture
```
backend/Acordanza.Api/    — ASP.NET Core Web API: EF Core + Npgsql, ASP.NET Core Identity, JWT auth
backend/Acordanza.Worker/ — drains the route-enrichment and route-import queues; its own project/image, no ASP.NET Core
backend/Acordanza.Core/   — the class library both share: EF models, DbContext, migrations
frontend/                 — React + TypeScript, built with Vite, package-managed with Bun
docker-compose.yml        — local PostgreSQL + Azurite (blob storage, the two worker queues)
```

## Setup

### 1. Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh) (v1.3+)
- [Docker](https://www.docker.com/) (for local Postgres)

### 2. Start Postgres
```
docker compose up -d
```

Uses the defaults in `.env.example` (copy to `.env` to override).

### 3. Backend
```
cd backend/Acordanza.Api
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"   # first time only
dotnet ef database update                                        # applies migrations
dotnet run
```
The API listens on `http://localhost:5073` (see `Properties/launchSettings.json`).

Accounts are created via a CLI command (no public registration endpoint); role defaults to `user` if omitted (other roles: `moderator`, `admin`):
```
dotnet run -- create-user <email> <password> [role]
```

#### Alternative: run the backend in a container

Instead of step 2 + step 3 above, run Postgres and the backend together in Docker:
```
echo "JWT_KEY=$(openssl rand -base64 48)" >> .env   # first time only
docker compose up -d --build
```
This uses `Dockerfile.dev` and `docker-compose.override.yml` (auto-merged by `docker compose up`, no flag needed): the API runs under `dotnet watch`, so edits under `backend/Acordanza.Api` hot-reload in the running container, and a debugger can attach to it (e.g. Rider's Docker Compose debug support). Migrations still run automatically on startup, so there's no separate `dotnet ef database update` step; account creation still needs the CLI command above, run inside the container: `docker compose exec backend dotnet run -- create-user <email> <password> [role]`.

Caveat: adding a new route (e.g. a new `MapGet`/`MapControllers` call or controller action) isn't picked up by hot reload — same as `dotnet watch` on a host, but the container has no interactive terminal to prompt for a restart. Run `docker compose restart backend` after adding a route.

### 4. Frontend
```
cd frontend
bun install
cp .env.example .env   # VITE_API_URL, defaults to http://localhost:5073/api
bun dev
```
Open `http://localhost:5173` in your browser and log in with an account created above.

## Deploying so friends can use it remotely
- The API and frontend deploy as two always-on Azure Container Apps, plus the worker (route enrichment and route import) as a separate, event-driven Container Apps Job (scales to zero, its own `acordanza-worker` image) — all three via a Terraform-managed pipeline in GitHub Actions, see `infra/README.md`. The frontend runs behind `nginx-unprivileged`, serving the Vite build with `VITE_API_URL` baked in at Docker build time from the API app's URL.
- Not using Azure: backend needs any host that runs .NET 10 + Postgres (Railway, Render, Fly.io, a VPS) with `ConnectionStrings:Default`, `Jwt:Key`, `Jwt:Issuer`/`Jwt:Audience`, and `Cors:AllowedOrigins` set via environment variables — the worker (`backend/Acordanza.Worker/`) is optional and can be skipped entirely (route enrichment and creating a route from a topo photo just stay unavailable) or run as a plain scheduled/cron process with `ConnectionStrings:Default` + `Storage:*` set. Frontend needs `bun run build` (outputs to `frontend/dist`) served as a static site (Netlify, Vercel, Cloudflare Pages, or behind the same reverse proxy as the API), with `VITE_API_URL` set to the deployed API's URL at build time.
- A spare Raspberry Pi / home server also works — run both processes there and use [Tailscale](https://tailscale.com) to share access without exposing it to the public internet.

## Extending it
Some natural next steps if you want to keep building:
- Photo uploads per route
- Map view (area locations via lat/lng + a map library)
- Per-user "want to climb" wishlist
- Export routes to CSV/GPX
- Multi-pitch route support (per-pitch grades/descriptions)
