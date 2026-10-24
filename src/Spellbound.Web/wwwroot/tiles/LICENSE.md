# Tile images

`steady.webp`, `shifting.webp` and `wild.webp` were made by Jakob Lomander Boman on 24 Oct 2026 with Stable Diffusion
XL base 1.0 (`sd_xl_base_1.0.safetensors`) in a local ComfyUI, using `tools/tile-art/generate.py` and `finish.py`.
To the extent possible under law, he dedicates them to the public domain under
[CC0 1.0](https://creativecommons.org/publicdomain/zero/1.0/).

How to make them again (the script holds the prompts):

| File | Candidate (seed) | Finish |
|---|---|---|
| `steady.webp` | `steady-2` (11001) | `--saturation 0.9 --lightness 62` |
| `shifting.webp` | `shifting-1` (22002) | `--saturation 0.9 --lightness 62` |
| `wild.webp` | `wild-3` (55005) | `--saturation 0.45 --lightness 62` |
