# Theme: The Burned Grimoire

Adopted 18 Oct 2026 from the owner's art proposal. The game reads like a dark, gritty spellbook that has been through
a fire: soot-black paper with a grain and a vignette, bone-coloured ink, iron-plate buttons, and each mage's pages
inked in their school's colour. It is all CSS on top of Bootstrap's dark mode; no JavaScript, and no images except
the map's painted tiles (the brief is in [tile art](tile-art.md)) and one optional painted backdrop. The page's
background comes in five looks the player picks between (below).

## Where it lives

- `src/Spellbound.Web/wwwroot/app.css`, section "The Burned Grimoire": the fonts, the tokens, and the rules that use
  them. The tokens are mapped onto Bootstrap's `--bs-*` variables, so plain Bootstrap markup picks them up.
- `src/Spellbound.Web/Components/Layout/MainLayout.razor`: wraps every page in `<div class="school-ink">`. The old game
  set `data-school="…"` on it to the signed-in mage's school, which sets `--school`; without one, `--school` is the
  soft ink. The new game can use the same hook, or drop it.
- `MainLayout.razor.css`: the top bar (and the old game's bottom navigation) as iron plates. The match page's own
  bottom bar (`.match-tabs`) and the action bar above it (`.action-bar`) are in `app.css`, "The match page at 360 px".
- `GrimFilters.razor`: the SVG filters, once in the layout.

The game was scrapped on 18 Oct 2026 (see the roadmap); the theme stays. The school inks and the game pieces below
were made for the old game.

## Looks

The owner found the original background muddy (24 Oct 2026): its warm, low-frequency soot noise and warm grain over
brown-black read as dirt blotches. Rather than pick a replacement from mock-ups (`tools/mockups/backgrounds.html`), the
game has five looks to swap between, and the owner chooses by playing in them. Each is a `[data-look="…"]` block in
`app.css`, section "The Burned Grimoire", after the `body` rule:

| Look | `data-look` | Background | Cost |
|---|---|---|---|
| Burned grimoire (default) | `grimoire` | the original: grain, a vignette and soot noise over `--paper` | about 1 KB of CSS |
| Sigil night | `sigil` | indigo-black #0f0e15, a cool grain, a faint summoning circle (inline SVG) behind the board, a soft violet glow | about 1.5 KB of CSS |
| Candlelit table | `table` | a warm pool of light on dark wood grain (gradients); the map sits on a brass-rimmed felt tray (`.board-tray`) | about 1 KB of CSS |
| Arcane dust | `dust` | blue-black #0a0a10, the cool grain, three tiled layers of star and ember motes over a faint nebula | about 1.3 KB of CSS |
| Painted rune-slate | `slate` | the painted backdrop `wwwroot/backdrops/rune-slate.webp`, fading into #0b0d10 below it | 27 KB, once |

All five blocks are in `app.css`, so every player downloads their CSS (about 5 KB before compression). The rune-slate
image is referenced only inside the `slate` selector, so only players who pick it fetch it; the browser's cache keeps
it after that. It is drawn at its own proportions over the first screen (`max(100vw, 430px)` wide), not stretched over a
long page. No look uses `background-attachment: fixed`: Android repaints a fixed background on every scroll, and iOS
ignores it.

Each look also overrides tokens for its cards, frames and plates, on `:root[data-look="…"]`:

| Token | Grimoire | Sigil night and Arcane dust | Painted rune-slate |
|---|---|---|---|
| `--paper-deep` | #1e1a15 | #17161f | #15181d |
| `--paper-edge`, `--bs-border-color` | #3b3229 | #37334a | #313840 |
| `--plate-top` | #2a241e | #22202c | #20252b |
| `--plate-bot` | #191511 | #13121a | #111418 |
| `--rivet` | #4d4135 | #3f3a52 | (grimoire's) |

Sigil night and Arcane dust also define `--grain-cool`, a neutral grain instead of the warm one. The candlelit table
keeps the grimoire's tokens.

**The switcher.** The account page has a "Look" section: one row per look with a swatch, a short line and its cost,
the one in use marked. A row is a plain link to `/look/{name}?back=/Account/Manage` (`Components/Looks.cs`), which
checks the name against the five, sets the `look` cookie (one year, SameSite=Lax, HttpOnly) and redirects back to a
local page. The choice is per device and not stored with the account, so it needs no table. `App.razor` reads the
cookie from the request and sets `data-look` on `<html>`, so a page is drawn in its look from the first paint; an
unknown or missing cookie gives the grimoire. The links skip Blazor's enhanced navigation, so the whole page is loaded
again in the new look.

**The backdrop.** `rune-slate.webp` (720 px wide, 27 KB) was painted with SDXL base 1.0 on a local ComfyUI by
`tools/tile-art/backdrop.py` and finished by `backdrop.py --finish` ([tile art](tile-art.md)). It is dedicated to the
public domain (CC0 1.0); its seed and settings are in `wwwroot/backdrops/LICENSE.md`. It is a trial: it has faux-text
blocks near the top and bottom, which a re-roll (another seed, on the owner's ComfyUI) or a centre crop would remove
if the look is kept.

## The raised board

The map's tiles stand like board-game pieces (owner's choice, 24 Oct 2026, from three board styles: flat, raised, and
raised and tilted). `HexMap.razor` draws them with plain polygons, no CSS `drop-shadow` filter (too slow on phones):

- every tile, dark ones too, stands on a near-black copy of itself 5 units lower (`.hex-depth`), its thickness, drawn
  first so it shows only below the bottom row and between tiles; the view box is 5 units taller for it;
- an unveiled tile has a bevel just inside its edge: a light line on its top-left three edges (`.bevel-light`) and a
  dark one on its bottom-right three (`.bevel-dark`).

At the 5-player map's 37 tiles (about 40 px a tile at 360 px wide) the bevel stays at the edge, clear of the art and
the labels.

## Tokens

| Token | Value | Use |
|---|---|---|
| `--paper` | #14110e | the page |
| `--paper-deep` | #1e1a15 | cards, fields |
| `--paper-edge` | #3b3229 | borders, rules |
| `--ink` | #dbcfb7 | text |
| `--ink-soft`, `--ink-faint` | lighter ink | labels, hints |
| `--school` | the mage's ink | primary buttons, focus ring, the active tab, a chosen option, your row in the rankings |

## School inks

| School | Ink | Token |
|---|---|---|
| Dawn | brass #c49a3e | `--radiant-ink` |
| Grove | lichen #7e9a52 | `--verdant-ink` |
| Ember | rust #c2502f | `--ember-ink` |
| Umbra | bruise #9273a8 | `--umbral-ink` |
| Veil | cold brine #5f8da2 | `--tidal-ink` |

## Fonts

Self-hosted variable woff2 files (Latin subset) in `wwwroot/fonts/`, each with its SIL Open Font License:

- **Grenze Gotisch**: blackletter, for page titles (`h1`, `h2`) and, in phase 2, battle stamps.
- **Grenze**: for mage names (`h1.mage-name`), labels, buttons and the navigation, uppercase and letter-spaced.
- **Alegreya** (roman and italic): the body text.

They replaced IM Fell English SC and the system sans-serif.

## Battle stamps (the new game)

The match page's report brands each battle the player fought with a stamp after its last line (`Verdicts.cs`, the
`.stamp` CSS): **Conquered** (you took the tile) or **Repelled** / **Thwarted** (your attack failed or took nothing)
when you attacked, **Held** or **Lost** when you defended. Battles between others stay plain lines.

## Sigils, the turn moon, the mana vial, battle stamps

Built for the old game and removed with it; they are in the git history at `b171e46`: `SigilGenerator.cs` (a mage's
brand drawn from its name, a byte-for-byte port of the proposal's script, with `SigilTests` and its fixtures),
`Sigil.razor`, the turn moon in `TurnsBar.razor`, `ManaVial.razor` and the battle stamps in `ReportView.razor`. Their
CSS is still in `app.css` (`.sigil`, `.turns .moon`, `.vial`, `.stamp`), unused until something uses it again. The
SVG filters stay in the layout.

## Phases

Phase 1 (fonts, palette, cards, buttons, school inks) and phase 2 (the game pieces above and the SVG filters) were
built for the old game. The new game has its first motion: as a round begins on the open match page, a banner
names it, a "+N" rises from every held tile and your mana shows what it gained (CSS only, played once, and off for
phones set to reduce motion). Still open, for whatever the new game needs: the rest of the motion (a flare after an
action, reports that burn in), a light variant, and a check on a real phone that
blackletter reads well at 360 px, that the SVG filters are fast enough, that the glyphs render, and, once the painted
tiles are in, that they read at 40–55 px wide.
