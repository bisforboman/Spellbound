# Spellbound

A turn-based, text-first multiplayer strategy game for phones, inspired by Archmage. It is a hobby project.

- **Gameplay:** spend regenerating turns to explore, build, meditate and cast spells. PvP comes in milestone 2.
- **Stack:** .NET 10, Blazor Server (interactive server rendering), embedded RavenDB, ASP.NET Core Identity.
- **Hosting plan:** one small VPS running Docker Compose with Caddy, installable on phones as a PWA.

## Getting started

Requires the .NET 10 SDK.

```powershell
./scripts/bootstrap.ps1     # one-time: creates the solution and project files
dotnet test
dotnet run --project src/Spellbound.Web
```

To use the admin page (`/admin`), list your account's email under `Admins` in `appsettings.json`, or set it as an
environment variable: `Admins__0=you@example.com`.

## Docs

- [Game design](docs/game-design.md)
- [Architecture](docs/architecture.md)
- [Roadmap](docs/roadmap.md)
