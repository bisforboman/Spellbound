"""Generates the rune-slate page backdrop (docs/theme.md) with SDXL base 1.0, and finishes it.

    python tools/tile-art/backdrop.py [out_dir]                 # on a local ComfyUI, two candidates per seed
    python tools/tile-art/backdrop.py --cpu out_dir seed...     # on the CPU with diffusers (about 6 min an image)
    python tools/tile-art/backdrop.py --finish <png>

Writes backdrop-<seed>[-<n>].png at 768x1344 into out_dir (default tools/tile-art/out, gitignored). The one in use is
backdrop-7302, made with --cpu (diffusers' noise differs from ComfyUI's, so a seed gives a different image in each).
--finish resizes it with Pillow to 720 wide, colour 0.55, brightness 0.42, WebP quality 60, into
src/Spellbound.Web/wwwroot/backdrops/rune-slate.webp. --cpu and --finish need a venv with torch, diffusers,
transformers, accelerate and pillow; --cpu downloads SDXL base 1.0 from Hugging Face the first time (about 7 GB).
"""

import json
import sys
import time
from pathlib import Path

from generate import call, graph

# The first backdrop (seed 7101) asked for a "board game table" and got faux-text panels, like a rulebook's, at its top
# and bottom; this prompt asks for a bare slab instead, and the negative names every kind of writing.
PROMPT = ("top-down view of an ancient dark slate stone slab, seamless stone surface filling the whole image, faintly "
          "engraved with a large arcane summoning circle, thin rune rings and geometric sigils, the engravings glowing "
          "very faintly cold teal, deep blue-black stone, subtle cracks, dim candlelight from above, dark fantasy, "
          "low contrast, very dark, the centre calm")
NEGATIVE = ("text, letters, words, writing, handwriting, inscription, caption, label, paragraph, typography, font, "
            "document, page, paper, panel, text box, card, book, map legend, diagram, infographic, user interface, "
            "bright, watermark, signature, people, hands, objects, candles, books, skull, saturated, purple, magenta, "
            "gold, frame, border, photograph")
SEEDS = (7301, 7302, 7303, 7304)


def cpu(out, seeds):
    """SDXL base 1.0 in bfloat16 on the CPU, with ComfyUI's sampler settings (DPM++ 2M Karras, 30 steps, cfg 7)."""
    import torch
    from diffusers import DPMSolverMultistepScheduler, StableDiffusionXLPipeline

    pipe = StableDiffusionXLPipeline.from_pretrained("stabilityai/stable-diffusion-xl-base-1.0",
                                                     torch_dtype=torch.bfloat16, variant="fp16", use_safetensors=True)
    pipe.scheduler = DPMSolverMultistepScheduler.from_config(pipe.scheduler.config, use_karras_sigmas=True)
    for seed in seeds:
        image = pipe(PROMPT, negative_prompt=NEGATIVE, width=768, height=1344, num_inference_steps=30,
                     guidance_scale=7.0, generator=torch.Generator().manual_seed(seed)).images[0]
        image.save(out / f"backdrop-{seed}.png")
        print(f"seed {seed}: done")


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
    if sys.argv[1:2] == ["--cpu"]:
        out = Path(sys.argv[2])
        out.mkdir(parents=True, exist_ok=True)
        return cpu(out, [int(seed) for seed in sys.argv[3:]] or SEEDS)
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
