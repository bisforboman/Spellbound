# Spellbound

A text-first multiplayer game for phones. It is a hobby project.

- **Gameplay:** a clean-slate design ([game design](docs/game-design.md)): a dark hex map, an opening draft, one
  stance per round, Imps and Golems, 8 rounds. The platform it runs on is built: sign-in, accounts, rounds, the admin
  page, backups and notifications. The old game is in the git history (see the roadmap).
- **Stack:** .NET 10, Blazor Server (interactive server rendering), PostgreSQL (plain Npgsql), sign-in with Google.
- **Hosting plan:** one small VPS running Docker Compose with Caddy, installable on phones as a PWA.

## Getting started

Requires the .NET 10 SDK and Docker (for a local PostgreSQL; any PostgreSQL 16 on localhost:5432 works too).

```powershell
./scripts/bootstrap.ps1     # one-time: creates the solution and project files
docker compose -f docker-compose.dev.yml up -d
dotnet test
dotnet run --project src/Spellbound.Web
```

Locally, sign in with the Development sign-in on the sign-in page (any email; no Google needed). To use the admin
page (`/admin`), list that email under `Admins` in `appsettings.json`, or set it as an environment variable:
`Admins__0=you@example.com`. Outside Development, sign-in needs a Google OAuth client (see [Deploying](docs/deploy.md)).

### Playing on your phone at home

To play from your phone on the same Wi-Fi, start the database as above, then run the app so it listens on your home
network and reloads when the code changes:

```powershell
docker compose -f docker-compose.dev.yml up -d
$env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 'true'
dotnet watch run --project src/Spellbound.Web --launch-profile lan
```

Small edits apply while it runs; bigger ones (a new type, a changed signature) rebuild and restart it without asking.
After a restart, reload the page on the phone.

Find your computer's address with `ipconfig` (Windows) or `ip addr` (Linux): the "IPv4 Address", such as
`192.168.1.20`. On your phone, open `http://192.168.1.20:5174`, sign in with the Development sign-in, then tap
**New match** and **Play bots**. The first time, Windows asks whether to let `Spellbound.Web` through the firewall: allow it on
private networks only. If the phone can't connect, check that Windows counts your Wi-Fi as a private network: on a
public one it blocks the phone without asking.

- Use it only on a network you trust: the Development sign-in lets anyone who can reach the page sign in as any email.
- Notifications and installing the app on the home screen don't work this way: browsers allow them only over HTTPS.

## Docs

- [Game design](docs/game-design.md) and the [paper prototype](docs/skirmish-paper-prototype.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Deploying](docs/deploy.md)
- [Theme](docs/theme.md)
