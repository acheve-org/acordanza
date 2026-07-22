# Acordanza — a shared climbing route log

A small private web app for logging and searching climbing routes with a few friends.
ASP.NET Core (.NET 10) Web API + PostgreSQL backend, React + Vite frontend.

## Features
- Add routes: name, crag, grade (any grading system), type (sport/trad/boulder/etc.), description, tags, first ascent
- Search & filter by name/description, crag, type, and tag
- Log ascents ("ticks") per route: who, when, rating, notes
- Real user accounts (JWT auth) — everyone who has an account can see and edit everything
- Accounts are admin-provisioned; there is no public sign-up

## Architecture
```
backend/Acordanza.Api/   — ASP.NET Core Web API: EF Core + Npgsql, ASP.NET Core Identity, JWT auth
frontend/                — React + TypeScript, built with Vite, package-managed with Bun
docker-compose.yml       — local PostgreSQL container
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

Accounts are created via a CLI command (no public registration endpoint):
```
dotnet run -- create-user <username> <password>
```

### 4. Frontend
```
cd frontend
bun install
cp .env.example .env   # VITE_API_URL, defaults to http://localhost:5073/api
bun dev
```
Open `http://localhost:5173` in your browser and log in with an account created above.

## Deploying so friends can use it remotely
- Both backend and frontend deploy together as two Azure Container Apps in the same environment, via a Terraform-managed pipeline in GitHub Actions — see `infra/README.md`. The frontend runs behind `nginx-unprivileged`, serving the Vite build with `VITE_API_URL` baked in at Docker build time from the API app's URL.
- Not using Azure: backend needs any host that runs .NET 10 + Postgres (Railway, Render, Fly.io, a VPS) with `ConnectionStrings:Default`, `Jwt:Key`, `Jwt:Issuer`/`Jwt:Audience`, and `Cors:AllowedOrigins` set via environment variables. Frontend needs `bun run build` (outputs to `frontend/dist`) served as a static site (Netlify, Vercel, Cloudflare Pages, or behind the same reverse proxy as the API), with `VITE_API_URL` set to the deployed API's URL at build time.
- A spare Raspberry Pi / home server also works — run both processes there and use [Tailscale](https://tailscale.com) to share access without exposing it to the public internet.

## Extending it
Some natural next steps if you want to keep building:
- Photo uploads per route
- Map view (crag locations via lat/lng + a map library)
- Per-user "want to climb" wishlist
- Export routes to CSV/GPX
- Multi-pitch route support (per-pitch grades/descriptions)
