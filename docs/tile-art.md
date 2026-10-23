# Tile art

The map can show a painted image for each of the three tile types instead of plain tiles with symbols. This page is
the brief for that art: hand it to an artist or use it with an image generator. The map is already prepared: drop
the three files in and it uses them (`Components/Game/TileArt.cs`). Until all three are there, it keeps the plain tiles.

## What to deliver

| File | Type | What it is in the game |
|---|---|---|
| `src/Spellbound.Web/wwwroot/tiles/steady.webp` | Steady | Always the same yield. Settled, safe, worked land. |
| `src/Spellbound.Web/wwwroot/tiles/shifting.webp` | Shifting | A yield that goes a little up or down. Restless but tame. |
| `src/Spellbound.Web/wwwroot/tiles/wild.webp` | Wild | A yield that swings a lot. Untamed, magical, dangerous. |

- **Format:** WebP (PNG works too, as `.png`), **512 × 512 px**, square, full bleed (no transparency needed).
- **Size:** under about 60 KB each, so a phone loads the map fast.
- **Licence:** the repository is public, so the images are published with it. They must be yours to publish:
  your own work, an artist's work you have the rights to share, or a generator whose terms let you redistribute the
  output. Add `src/Spellbound.Web/wwwroot/tiles/LICENSE.md` saying who made them and under what licence.

## The shape and what covers it

The game cuts a hexagon out of the square, pointy end up: corners at (50%, 0), (93.3%, 25%), (93.3%, 75%),
(50%, 100%), (6.7%, 75%) and (6.7%, 25%). Everything outside is never seen, so paint to the edges and don't frame
it.

On top of the art, the game draws:

- **the middle:** a dark round seal with the tile's yield, about 16% of the height across;
- **the top fifth:** the tile's name ("C3");
- **the bottom fifth:** units or a monster guard ("3i", "☠3");
- **the edge:** a ring in the owner's colour when someone holds the tile, and the outlines for "up for auction" and
  "selected".

So put the subject in the ring between the middle and the edge, and keep the middle and top quiet.

## Style

The game's look is **the Burned Grimoire** ([theme](theme.md)): a dark, gritty spellbook that has been through a fire.
Soot-black, charred leather, bone-coloured ink, old iron.

- **Dark and low contrast:** the page is near-black (#14110e) and text is bone (#dbcfb7). Tiles should sit in that
  range, darker than the text, so the numbers on top stay readable.
- **Readable small:** on a phone a tile is only about 40–55 px wide. Broad shapes and one clear subject, not fine detail.
  The three must be told apart at a glance, by shape and mood more than colour.
- **Keep clear of the seat colours.** Ownership is a ring in one of five inks: tarnished brass (#c49a3e), lichen green
  (#7e9a52), forge rust (#c2502f), bruise purple (#9273a8) and cold brine blue (#5f8da2). None of them should be a
  tile's main colour, or the ring gets lost.
- **No text, no border, no frame** in the image. Dark (unrevealed) tiles stay the game's own soot; they need no art.
- **Seen from above or at a slight angle**, like a board game tile.

## Prompts

A starting point for a generator; adjust to taste. Use the same style sentence for all three, so they match.

> Style (all three): a single hexagonal board-game tile seen from above, painted in a dark, gritty, burned-spellbook
> style: soot-black and charred-leather tones, muted bone highlights, ink-etched texture, low contrast, no text, no
> border, full bleed square image, the centre kept calm and empty.

- **Steady:** "…of settled farmland: ploughed furrows, a low dry-stone wall and a small stone tower, quiet and
  orderly, earthy browns."
- **Shifting:** "…of restless dunes and marsh: rippling sand, a still pool and a few reeds, slate and ash greys."
- **Wild:** "…of untamed wild land: twisted thorns around a faintly glowing rift, scattered sparks of magic, deep
  ashen violet-black (not bright purple)."

## Trying it

Put the three files in `src/Spellbound.Web/wwwroot/tiles/`, restart the app (it looks for them at startup) and open
a match. The playtest (`tools/Spellbound.Playtest`) takes screenshots of the map at phone size, including the
5-player map, where tiles are smallest.
