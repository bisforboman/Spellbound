---
name: reload
description: Bring new code into the game the owner plays locally (on this computer or a phone on the home Wi-Fi), from main or from a PR. Use when asked to reload, update or refresh the local game, or to try a PR in it.
---

# Reload the local game

The owner's local game runs from the main checkout, `C:\dev\Spellbound` (not a worktree), under `dotnet watch` on
port 5174 with the `lan` profile. The watcher picks up new files by itself, so reloading is mostly a `git` step there.

## Steps

1. **Get the code**, in `C:\dev\Spellbound`. If it has uncommitted changes, stop and tell the owner; never stash,
   reset or discard them.
   - No argument: `git switch main`, then `git pull --ff-only`.
   - A PR number (`/reload 107`): `gh pr checkout 107`.
2. **Check the game answers:** `curl -s -o /dev/null -w "%{http_code}" http://localhost:5174/` gives `302`. Then the
   watcher reloads or restarts it on its own; wait a few seconds and check again.
3. **Only if it doesn't answer**, start it. (`dotnet watch` sometimes crashes on a big pull; its old tab then sits at a
   prompt, and starting it again is the fix.)
   - The database: `docker ps` for a PostgreSQL on port 5432. If there is one, use it (it may belong to another
     compose project; a second one would clash on the port). If there is none, run
     `docker compose -f docker-compose.dev.yml up -d` in `C:\dev\Spellbound`.
   - The watcher, in a Terminal-panel tab (`run_in_terminal`, cwd `C:\dev\Spellbound`) so the owner can stop it:

     ```powershell
     $env:DOTNET_WATCH_RESTART_ON_RUDE_EDIT = 'true'; dotnet watch run --project src/Spellbound.Web --launch-profile lan
     ```

     Wait until `curl` gives `302`.
4. **Report** what is now running (the commit or PR), and the phone address: `ipconfig`'s Wi-Fi IPv4 address,
   `http://<address>:5174`. Remind the owner to reload the page on the phone.
