# Spellbound roadmap

## The game was scrapped (18 Oct 2026)

The owner is designing the game again from the bottom. The old game and its designs were removed: the Realm (an
Archmage-style economy and PvP game, built through milestones M0–M5) and its half-built rework as Mageborn (M7: the
turn bank, season phases, explore decay, the Vigil, the ledger preview, the Chronicle, published net power).
Skirmish (M6) had been removed earlier the same day. All of it is in the git history at `b171e46`, with the designs
in `docs/game-design.md`, `docs/mageborn-design.md`, `docs/realm-rework.md` and the balance studies.

## What stays: the platform
- [x] Blazor Server on .NET 10, PostgreSQL through plain Npgsql, numbered SQL migrations ([architecture](architecture.md))
- [x] Sign in with Google (and a Development-only sign-in for local runs and tests); account page, data download and
  account deletion
- [x] The game host (`Hosting/`): games and rounds kept running by `RoundKeeper`, the admin page (rounds, bans,
  multi-account signals, problems), rate limits, alerts, nightly backups with a tested restore, web push notifications
- [x] The privacy notice (`/privacy`), written from what the code stores
- [x] Docker, Caddy and the deploy guide ([deploy](deploy.md)); CI builds, tests and smoke-tests the image
- [x] The art theme "The Burned Grimoire" ([theme](theme.md)): fonts, palette, cards, buttons; its game pieces (sigils,
  turn moon, mana vial, battle stamps) went with the game and are in the git history
- [x] The playtest tool (`tools/Spellbound.Playtest`), cut down to the platform's pages
- [x] `PlaceholderGame`: the game's place, so the platform runs, until the new design has one

## M8: The new game
To be planned once the new design is written. Before building it, decide:
- [ ] The design document (rules and starting numbers), and where it lives in `docs/`
- [ ] The architecture rules in `CLAUDE.md` marked "to be redefined" (turns, balance numbers, the domain's actions)
- [ ] Which old pieces to bring back from the git history, if any: `DomainJson` (jsonb snapshots of domain objects),
  the action log, the balance simulation, the sigil generator, the ledger preview

## Waiting on the owner (before any deploy)
- [ ] A VPS and a domain pointing at it (see [deploy guide](deploy.md))
- [ ] A Google OAuth client for sign-in (see [deploy guide](deploy.md))
- [ ] Read the privacy notice (`/privacy`) and fill in its `PRIVACY_*` settings: operator, contact, hosting, off-site
  backups (see [deploy guide](deploy.md#privacy-notice))
- [ ] Terms and rules page, and an "ask an admin" contact path (the server sends no email)

## Engineering
- Persistence: plain Npgsql with Google sign-in, decided 17 Oct 2026 after comparing alternatives and similar games
  ([research](research/persistence.md))
- Playtest tool (`tools/Spellbound.Playtest`): plays every page at 360 px in a real browser; not in CI yet (it needs
  `playwright install --with-deps` there)
- Tests: most of their time is the app starting once per test; share one app per test class if speed matters

## Later
- Real email (an SMTP service), if the game ever needs it
- More sign-in options (Apple, passkeys), linked to the same account
- Move `Hosting/` into its own reusable project if a separate game is ever planned (see architecture, "The game host
  boundary")
