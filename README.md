# Spellbound

A text-first multiplayer game for phones. It is a hobby project.

- **Gameplay:** a clean-slate design ([game design](docs/game-design.md)): a dark hex map, an opening mana auction, one
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

## Docs

- [Game design](docs/game-design.md) and the [paper prototype](docs/skirmish-paper-prototype.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Deploying](docs/deploy.md)
- [Theme](docs/theme.md)
