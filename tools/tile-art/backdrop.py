"""Generates the rune-slate page backdrop (docs/theme.md) on a local ComfyUI with SDXL base, and finishes it.

    python tools/tile-art/backdrop.py [out_dir]

Writes backdrop-<seed>-<n>.png at 768x1344 into out_dir (default tools/tile-art/out, gitignored). The one in use is
backdrop-7101-1; finish it with Pillow (resize to 720 wide, colour 0.55, brightness 0.42, WebP quality 60) into
src/Spellbound.Web/wwwroot/backdrops/rune-slate.webp, as --finish <png> does.
"""

import json
import sys
import time
from pathlib import Path

from generate import call, graph

PROMPT = ("top-down view of an ancient dark slate stone table surface, faintly engraved with a large arcane summoning "
          "circle, thin rune rings and geometric sigils, the engravings glowing very faintly cold teal, deep blue-black "
          "stone, subtle cracks, dim candlelight from above, dark fantasy board game table, low contrast, very dark, "
          "the centre calm")
NEGATIVE = ("bright, text, letters, watermark, signature, people, hands, objects, candles, books, skull, saturated, "
            "purple, magenta, gold, frame, border, photograph")
SEEDS = (7101, 7202)


def finish(png):
    from PIL import Image, ImageEnhance  # not on the system Python; use a venv

    image = Image.open(png).convert("RGB")
    image = image.resize((720, round(image.height * 720 / image.width)), Image.LANCZOS)
    image = ImageEnhance.Brightness(ImageEnhance.Color(image).enhance(0.55)).enhance(0.42)
    target = Path(__file__).parents[2] / "src/Spellbound.Web/wwwroot/backdrops/rune-slate.webp"
    image.save(target, "WEBP", quality=60, method=6)
    print(f"{target}: {target.stat().st_size // 1024} KB")


def main():
    if sys.argv[1:2] == ["--finish"]:
        return finish(sys.argv[2])
    out = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent / "out")
    out.mkdir(parents=True, exist_ok=True)
    for seed in SEEDS:
        prompt = graph(PROMPT, NEGATIVE, seed, 2)
        prompt["5"]["inputs"].update(width=768, height=1344)
        queued = json.loads(call("POST", "/prompt", json.dumps({"prompt": prompt})))["prompt_id"]
        while not (history := json.loads(call("GET", f"/history/{queued}"))).get(queued):
            time.sleep(2)
        images = [i for node in history[queued]["outputs"].values() for i in node.get("images", [])]
        for n, image in enumerate(images, 1):
            query = f"/view?filename={image['filename']}&subfolder={image['subfolder']}&type={image['type']}"
            (out / f"backdrop-{seed}-{n}.png").write_bytes(call("GET", query))
        print(f"seed {seed}: {len(images)} candidates")


if __name__ == "__main__":
    main()
