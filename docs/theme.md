# Theme: The Burned Grimoire

Adopted 18 Oct 2026 from the owner's art proposal. The game reads like a dark, gritty spellbook that has been through
a fire: soot-black paper with a grain and a vignette, bone-coloured ink, iron-plate buttons, and each mage's pages
inked in their school's colour. It is all CSS on top of Bootstrap's dark mode; no JavaScript, and no images except
the map's painted tiles once they exist (the brief is in [tile art](tile-art.md)).

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
