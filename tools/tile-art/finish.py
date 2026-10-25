"""Turns a chosen candidate into a tile image: 512 px, toned down to the Burned Grimoire's range, WebP under 60 KB.

    python tools/tile-art/finish.py <candidate.png> <steady|shifting|wild> [--saturation 0.5] [--lightness 62]

Lightness is the target mean (0-255): the page is #14110e (about 17) and text #dbcfb7 (about 206), and the tiles
sit well below the text so the numbers on them stay readable. Needs Pillow.
"""

import argparse
import io
from pathlib import Path

from PIL import Image, ImageEnhance, ImageStat

TILES = Path(__file__).resolve().parents[2] / "src/Spellbound.Web/wwwroot/tiles"
MAX_BYTES = 60_000


def finish(source, saturation, lightness):
    image = Image.open(source).convert("RGB").resize((512, 512), Image.LANCZOS)
    image = ImageEnhance.Color(image).enhance(saturation)
    mean = sum(ImageStat.Stat(image).mean) / 3
    return ImageEnhance.Brightness(image).enhance(lightness / mean)


def webp(image):
    for quality in range(90, 20, -5):
        buffer = io.BytesIO()
        image.save(buffer, "WEBP", quality=quality, method=6)
        if buffer.tell() <= MAX_BYTES:
            return buffer.getvalue()
    raise RuntimeError("cannot get under 60 KB")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("source")
    parser.add_argument("type", choices=["steady", "shifting", "wild"])
    parser.add_argument("--saturation", type=float, default=1.0)
    parser.add_argument("--lightness", type=float, default=62)
    parser.add_argument("--out", type=Path, default=TILES)
    args = parser.parse_args()
    data = webp(finish(args.source, args.saturation, args.lightness))
    args.out.mkdir(parents=True, exist_ok=True)
    (args.out / f"{args.type}.webp").write_bytes(data)
    print(f"{args.type}.webp: {len(data) // 1000} KB")


if __name__ == "__main__":
    main()
