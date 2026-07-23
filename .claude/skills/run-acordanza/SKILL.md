---
name: run-acordanza
description: Build, start, and drive the Acordanza climbing-route-log app (ASP.NET Core API + React frontend + Postgres). Use when asked to run Acordanza, start the backend/frontend, take a screenshot of the app, or verify a change works end-to-end in the browser.
---

Acordanza is a two-part app: `backend/Acordanza.Api` (ASP.NET Core Web
API on .NET 10, EF Core + Npgsql) and `frontend/` (React + Vite, run
with Bun). Drive the running app with the Playwright script
`.claude/skills/run-acordanza/driver.mjs` — it logs in, adds a route,
logs/deletes a tick, edits the route, exercises the filters, and
deletes the route, taking a screenshot at each step. All paths below
are relative to the repo root.

## Prerequisites

- .NET 10 SDK, on `PATH` (`dotnet --version`). `dotnet ef` works too,
  with no extra setup — it resolves through `dotnet`'s own global-tool
  manifest rather than `PATH`, so it doesn't matter that the raw
  `dotnet-ef` binary under `~/.dotnet/tools` isn't independently
  resolvable (that entry sits unexpanded — literally `~/.dotnet/tools`
  — in `$PATH`, which no shell expands during command lookup; harmless
  since nothing here invokes `dotnet-ef` directly).
- Bun (already on `PATH` via proto shim).
- Docker Desktop, running (`open -a Docker` if not; poll `docker info`
  until it succeeds — takes a few seconds).

## Setup (one-time per machine)

Start Postgres:
```bash
docker compose up -d
```

Set a JWT signing secret (stored via `dotnet user-secrets`, not in
`appsettings.json`):
```bash
cd backend/Acordanza.Api
dotnet user-secrets set "Jwt:Key" "$(openssl rand -base64 48)"
```

Apply EF Core migrations and create a login (accounts are
admin-provisioned — there's no public register endpoint):
```bash
dotnet ef database update
dotnet run -- create-user hugo testpass123
# → "Username 'hugo' is already taken." on repeat runs is fine — ignore it.
```

Install the frontend's own deps, and the driver's (Playwright):
```bash
cd frontend && bun install && cp -n .env.example .env
cd ../.claude/skills/run-acordanza && bun install
bunx playwright install chromium   # only needed once per machine
```

## Build

No separate build step is needed to run in dev mode. To sanity-check
a production build compiles:
```bash
cd backend/Acordanza.Api && dotnet build   # from repo root
cd frontend && bun run build
```

## Run (agent path)

Start both dev servers in the background and wait for each to answer:

```bash
cd backend/Acordanza.Api
nohup dotnet run > /tmp/acordanza-api.log 2>&1 & disown
i=0; while [ $i -lt 40 ]; do curl -sf -o /dev/null -w "%{http_code}" http://localhost:5073/api/auth/me 2>/dev/null | grep -q 401 && break; sleep 1; i=$((i+1)); done
```

```bash
cd frontend
nohup bun dev --port 5173 > /tmp/acordanza-frontend.log 2>&1 & disown
i=0; while [ $i -lt 30 ]; do curl -sf http://localhost:5173 >/dev/null 2>&1 && break; sleep 1; i=$((i+1)); done
```

Then drive it:
```bash
cd .claude/skills/run-acordanza
bun run driver.mjs
```

Prints `PASS: golden path completed with no console errors` and exits
0 on success, or `FAIL: <assertion message>` with exit code 1.
Screenshots land in `.claude/skills/run-acordanza/shots/`, numbered in
order (`01-login-screen.png` … `07-route-deleted.png`); the last one
before a failure is `NN-failure-state.png`.

Override credentials/URL if needed:
```bash
ACORDANZA_USER=hugo ACORDANZA_PASSWORD=testpass123 ACORDANZA_FRONTEND_URL=http://localhost:5173 bun run driver.mjs
```

Stop both servers when done:
```bash
lsof -ti:5073 -sTCP:LISTEN | xargs -r kill
lsof -ti:5173 -sTCP:LISTEN | xargs -r kill
```
(Postgres can stay up — `docker compose down` only if you want to
tear it down too.)

## Run (human path)

```bash
docker compose up -d
cd backend/Acordanza.Api && dotnet run           # http://localhost:5073
cd frontend && bun dev                           # http://localhost:5173, Ctrl-C to stop
```
Open `http://localhost:5173`, log in with the account created above.

## Test

No automated test suite exists yet for either project (this is a
freshly migrated app) — `driver.mjs` is currently the only end-to-end
check. `dotnet build` / `bun run build` are the closest thing to unit
verification right now.

## Gotchas

- **No `timeout`/`gtimeout` binary on this Mac** (no coreutils
  installed) — the wait-for-server steps use a manual `while` loop with
  a counter instead of `timeout 40 bash -c '...'`; don't reintroduce
  `timeout`, it'll fail with `command not found: timeout`.
- **`$!` after `nohup dotnet run &`** is fine to capture, but killing by
  port (`lsof -ti:PORT -sTCP:LISTEN | xargs kill`) is more reliable than
  tracking the PID, especially across separate background Bash calls.
- **JSON casing**: the API's C# DTOs use PascalCase properties, but
  ASP.NET Core's default `JsonSerializerOptions` are case-insensitive
  on the way in and camelCase on the way out — the frontend (and the
  driver, if it ever calls the API directly) should just use camelCase
  keys (`gradeSystem`, `firstAscent`, etc.), never snake_case like the
  old Express version used internally.
- **Docker Desktop cold start**: `docker compose up -d` fails with a
  socket-connect error if Docker Desktop isn't running yet. `open -a
  Docker` then poll `docker info` — it's usually ready within ~5s, but
  don't assume it's instant.
- **First Playwright run downloads Chromium** (~170MB) via `bunx
  playwright install chromium` — do this once per machine, it's cached
  under `~/Library/Caches/ms-playwright`.
- **The delete-route confirm dialog**: `DetailModal`'s delete button
  triggers a native `confirm()`. Register `page.once('dialog', d =>
  d.accept())` *before* the click that triggers it — Playwright
  auto-dismisses dialogs with no handler attached in time.
- **Vite prints a Node version warning** on this machine (`Node.js
  22.6.0`, Vite wants `20.19+`/`22.12+`) — it's noise, the dev server
  still starts and serves fine; don't treat it as a failure signal.

## Troubleshooting

- **Login stuck at 401 / driver fails at `Sesión iniciada como hugo`**: the
  `hugo` user doesn't exist yet in this Postgres volume (e.g. after
  `docker compose down -v`). Re-run `dotnet run -- create-user hugo
  testpass123` from `backend/Acordanza.Api`.
- **`dotnet ef` not found**: install the tool once —
  `dotnet tool install --global dotnet-ef` (needs `dotnet` on `PATH`;
  `dotnet ef` then works regardless of whether `dotnet-ef` itself is
  independently reachable on `PATH`).
- **Backend fails to start with a `ConnectionStrings:Default` or
  `Jwt:Key` error**: Postgres isn't up (`docker compose up -d`) or the
  user-secret was never set (see Setup above).
