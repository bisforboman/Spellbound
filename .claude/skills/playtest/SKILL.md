---
name: playtest
description: Play Spellbound end to end in a real phone-sized browser and check every page. Use to run or verify the app, to look at a UI change, before opening a PR that changes pages, or when asked to playtest.
---

# Playtest

`tools/Spellbound.Playtest` plays the game the way players would. It:

1. creates a fresh `spellbound_playtest` database;
2. starts the app with fast rules (a turn a second, no protection, plenty of gold and mana);
3. signs in three players at 360×740 (one of them an admin);
4. visits every page and takes every kind of action: create a mage, explore, build, meditate, cast, recruit, attack,
   hex, the news banner, the hall of fame, account and data download, privacy, the admin page, and ending the round;
5. checks the admin page's **Problems** list at the end.

Every step is screenshotted and checked. A step fails on any of these: sideways scrolling at 360 px, Blazor's error
bar, console errors, failed requests or 5xx answers. Tap targets under 44 px are listed as warnings. Errors in the
app's own log fail the run.

## Run it

PostgreSQL must be running.

- **In a cloud session:** start the preinstalled cluster.

  ```sh
  su postgres -c "/usr/lib/postgresql/16/bin/pg_ctl -D /var/lib/pgtest/data -l /var/lib/pgtest/log start"
  export SPELLBOUND_TEST_POSTGRES="Host=localhost;Username=postgres;Password=postgres"
  ```

- **On your own machine:** `docker compose -f docker-compose.dev.yml up -d`, and the defaults work.

Then:

```sh
dotnet run --project tools/Spellbound.Playtest
```

Options:
- `--headed` shows the browser.
- `--no-app` tests an app you already started; pass its address with `--url`.
- `--out <dir>` changes the output folder.

**Exit code:** 0 when every step passed and the app logged no errors, otherwise 1.

## Read the result

Everything goes to `tools/Spellbound.Playtest/out/` (gitignored):

- `report.md`: each step, its problems and warnings.
- `NN-player-step.png`: a full-page screenshot per step, at 2× scale.
- `app.log`: the app's console output.

Before opening a PR that changes pages, run it, read `report.md`, and **look at the screenshots of the pages you
changed** with the Read tool. A pass only means nothing broke, not that the page looks right.

## Browser

The tool uses, in order:
1. `PLAYTEST_CHROMIUM`, if set;
2. the cloud sandbox's `/opt/pw-browsers/chromium`;
3. Playwright's own browser. On your own machine, install it once after a build:
   `pwsh tools/Spellbound.Playtest/bin/Debug/net10.0/playwright.ps1 install chromium`.

## Extend it

The script is `Playtest.PlayAsync` in `tools/Spellbound.Playtest/Program.cs`. A new page or action gets a
`StepAsync(player, "name", async page => …)`:
- open the page with `GoAsync`, which waits for Blazor's live connection so clicks aren't lost;
- run an action with `ActAsync` or `ClickAndReportAsync`: they wait for the action's report, and a failed report
  fails the step.

Choose elements by role or text, as a player would see them.
