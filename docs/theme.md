# Theme: The Burned Grimoire

Adopted 18 Oct 2026 from the owner's art proposal. The game reads like a dark, gritty spellbook that has been through
a fire: soot-black paper with a grain and a vignette, bone-coloured ink, iron-plate buttons, and each mage's pages
inked in their school's colour. It is all CSS on top of Bootstrap's dark mode; no images, no JavaScript.

## Where it lives

- `src/Spellbound.Web/wwwroot/app.css`, section "The Burned Grimoire": the fonts, the tokens, and the rules that use
  them. The tokens are mapped onto Bootstrap's `--bs-*` variables, so plain Bootstrap markup picks them up.
- `src/Spellbound.Web/Components/Layout/MainLayout.razor`: wraps every page in `<div class="school-ink"
  data-school="…">` with the signed-in mage's school, which sets `--school`. Before a mage exists, `--school` falls
  back to the soft ink.
- `MainLayout.razor.css`: the top bar and the bottom navigation as iron plates.

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

## Phases

See the roadmap, M7 "Look and feel": phase 1 (this) is fonts, palette, cards, buttons and school inks; phase 2 adds
the generated sigils, the turn moon, the mana vial, battle stamps and SVG filters; motion and a light variant come
later. Still to check on a real phone: that blackletter reads well at 360 px, that SVG filters are fast enough, and
that the glyphs render.
