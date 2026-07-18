# Topo — a shared climbing route log

A small private web app for logging and searching climbing routes with a few friends.
Node.js + Express + SQLite backend, plain HTML/CSS/JS frontend. No build step.

## Features
- Add routes: name, crag, grade (any grading system), type (sport/trad/boulder/etc.), description, tags, first ascent
- Search & filter by name/description, crag, type, and tag
- Log ascents ("ticks") per route: who, when, rating, notes
- Single shared passcode login — everyone who knows the code can see and edit everything
- Data lives in a local SQLite file (`climbing.db`) — no external database needed

## Setup

1. Install [Bun](https://bun.sh) (v1.3+).
2. In this folder, install dependencies:
   ```
   bun install
   ```
3. Copy the example environment file and edit it:
   ```
   cp .env.example .env
   ```
   Then open `.env` and set:
   - `PASSCODE` — the shared password your friends will use to log in
   - `SESSION_SECRET` — any random string (used to sign login cookies)
   - `PORT` — defaults to 3000

4. Start the server:
   ```
   bun start
   ```
   or, for auto-reload during development:
   ```
   bun run dev
   ```
5. Open `http://localhost:3000` in your browser.

The first time you open it, you'll be asked for your name and the passcode. Your name is attached to routes you add and ascents you log.

## Deploying so friends can use it remotely

This app runs anywhere Node.js runs. Easiest free/cheap options:
- **Railway** or **Render**: connect this folder as a GitHub repo, set the same environment variables in their dashboard, deploy. Note: on most platforms the filesystem resets on redeploy, so for anything beyond casual use, mount a persistent volume for `climbing.db` (both Railway and Render support this) — otherwise your data will vanish on the next deploy.
- **A spare Raspberry Pi / home server**: just run `node server.js` there and use a tool like [Tailscale](https://tailscale.com) to share access with friends without exposing it to the public internet.

## Project structure
```
server.js       — Express app: auth, routes API, ticks API
db.js           — SQLite schema + connection
public/
  index.html    — page structure
  style.css     — styling
  app.js        — frontend logic (fetch calls, rendering, modals)
```

## Extending it
Some natural next steps if you want to keep building:
- Photo uploads per route
- Map view (crag locations via lat/lng + a map library)
- Per-user "want to climb" wishlist
- Export routes to CSV/GPX
- Multi-pitch route support (per-pitch grades/descriptions)
