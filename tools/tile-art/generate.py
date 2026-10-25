"""Generates candidates for the three tile images (docs/tile-art.md) on a local ComfyUI with SDXL base.

    python tools/tile-art/generate.py [out_dir]

Writes <type>-<n>.png at 1024 px into out_dir (default tools/tile-art/out, gitignored). Pick one per type and
run finish.py on it; the ones in use are steady-2, shifting-1 and wild-3 (see wwwroot/tiles/LICENSE.md). Talks to ComfyUI with http.client: urllib honours this machine's proxy even for 127.0.0.1
and hangs (found in NavalBattler's tools/assetpipeline/comfy_ship.py).
"""

import http.client
import json
import sys
import time
from pathlib import Path

SERVER = "127.0.0.1:8188"
# The doc's style sentence, less "hexagonal board-game tile": with it SDXL paints hex grids and parchment. The game
# cuts the hexagon itself.
STYLE = ("top-down aerial view of a patch of dark fantasy land, filling the whole frame edge to edge, painted in a "
         "dark, gritty, burned-spellbook style: soot-black and charred-leather tones, muted bone highlights, "
         "ink-etched texture, low contrast, dim, the centre kept calm and empty")
# Subject, seed and negative. Wild's negative leaves out "high contrast, saturated, bright purple": with them it gets
# no glow at all; without them it comes out magenta, which finish.py --saturation 0.45 brings back to ash-violet.
NEGATIVE = ("bright, high contrast, saturated, white background, text, letters, numbers, border, frame, outline, "
            "edge, bevel, hexagon, hex grid, tiles, map, parchment, paper, vignette, sky, horizon, moon, clouds, "
            "perspective, watermark, signature, photograph, 3d render, glossy, bright purple, gold, brass")
WILD_NEGATIVE = NEGATIVE.replace("high contrast, saturated, ", "").replace("bright purple, ", "")
TILES = {
    "steady": ("settled farmland: ploughed furrows, a low dry-stone wall and a small stone tower, quiet and orderly, "
               "earthy browns", 11001, NEGATIVE),
    "shifting": ("restless dunes and marsh: rippling sand, a still pool and a few reeds, slate and ash greys", 22002,
                 NEGATIVE),
    "wild": ("untamed wild land: twisted black thorns around a faintly glowing violet rift, a crack of eerie violet "
             "light, scattered sparks of magic, deep ashen violet-black", 55005, WILD_NEGATIVE),
}


def graph(prompt, negative, seed, batch):
    return {
        "4": {"class_type": "CheckpointLoaderSimple", "inputs": {"ckpt_name": "sd_xl_base_1.0.safetensors"}},
        "5": {"class_type": "EmptyLatentImage", "inputs": {"width": 1024, "height": 1024, "batch_size": batch}},
        "6": {"class_type": "CLIPTextEncode", "inputs": {"text": prompt, "clip": ["4", 1]}},
        "7": {"class_type": "CLIPTextEncode", "inputs": {"text": negative, "clip": ["4", 1]}},
        "3": {"class_type": "KSampler", "inputs": {
            "seed": seed, "steps": 30, "cfg": 7.0, "sampler_name": "dpmpp_2m", "scheduler": "karras", "denoise": 1.0,
            "model": ["4", 0], "positive": ["6", 0], "negative": ["7", 0], "latent_image": ["5", 0]}},
        "8": {"class_type": "VAEDecode", "inputs": {"samples": ["3", 0], "vae": ["4", 2]}},
        "9": {"class_type": "SaveImage", "inputs": {"filename_prefix": "spellbound-tile", "images": ["8", 0]}},
    }


def call(method, path, body=None):
    connection = http.client.HTTPConnection(SERVER, timeout=600)
    try:
        connection.request(method, path, body, {"Content-Type": "application/json"} if body else {})
        response = connection.getresponse()
        data = response.read()
        if response.status != 200:
            raise RuntimeError(f"{method} {path}: {response.status} {data[:500]!r}")
        return data
    finally:
        connection.close()


def main():
    out = Path(sys.argv[1] if len(sys.argv) > 1 else Path(__file__).parent / "out")
    out.mkdir(parents=True, exist_ok=True)
    for name, (subject, seed, negative) in TILES.items():
        prompt = f"{STYLE}, a landscape of {subject}"
        body = json.dumps({"prompt": graph(prompt, negative, seed, 4)})
        queued = json.loads(call("POST", "/prompt", body))["prompt_id"]
        while not (history := json.loads(call("GET", f"/history/{queued}"))).get(queued):
            time.sleep(2)
        images = [i for node in history[queued]["outputs"].values() for i in node.get("images", [])]
        for n, image in enumerate(images, 1):
            query = f"/view?filename={image['filename']}&subfolder={image['subfolder']}&type={image['type']}"
            (out / f"{name}-{n}.png").write_bytes(call("GET", query))
        print(f"{name}: seed {seed}, {len(images)} candidates")


if __name__ == "__main__":
    main()
