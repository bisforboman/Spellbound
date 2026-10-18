# Spellbound

A turn-based, text-first multiplayer strategy game for phones, inspired by Archmage. It is a hobby project.

- **Gameplay:** spend regenerating turns to explore, build, meditate and cast spells. PvP comes in milestone 2.
- **Stack:** .NET 10, Blazor Server (interactive server rendering), PostgreSQL with EF Core, sign-in with Google.
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

- [Game design](docs/game-design.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
- [Deploying](docs/deploy.md)
- [Balance findings](docs/balance.md) (from a simulation; rerun with `dotnet run --project tools/Spellbound.Balance`)
