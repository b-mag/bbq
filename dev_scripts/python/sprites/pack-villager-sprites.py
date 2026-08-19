"""Turn generated villager portraits into 128x256 character sheets (D/L/R/U + attack)."""
from __future__ import annotations

import importlib.util
import os

from PIL import Image, ImageEnhance, ImageOps

SPEC = os.path.join(os.path.dirname(__file__), "pack-sprite-assets.py")
spec = importlib.util.spec_from_file_location("pack_sprite_assets", SPEC)
mod = importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(mod)

SRC_DIR = r"C:\Users\Brandon\.cursor\projects\c-Users-Brandon-VS-Code-Projects-carcosa-bbq\assets"
OUT_DIR = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\sprites"
CELL = 32
FRAMES = 4
DIRS = 4

SHEETS = (
    ("villager_ash_src.png", "villager_ash.png"),
    ("villager_dock_src.png", "villager_dock.png"),
    ("satyr_mask_src.png", "satyr_mask.png"),
    ("satyr_horn_src.png", "satyr_horn.png"),
)


def strip_magenta(img: Image.Image) -> Image.Image:
    pix = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = pix[x, y]
            if r > 170 and b > 170 and g < 140:
                pix[x, y] = (r, g, b, 0)
    return img


def pose(src: Image.Image, facing: int, frame: int) -> Image.Image:
    img = src.copy()
    if facing == 1:
        img = ImageOps.mirror(img)
    if facing == 3:
        img = ImageEnhance.Brightness(img).enhance(0.62)
        img = ImageEnhance.Color(img).enhance(0.55)
    bob = (0, -1, 0, 1)[frame % 4]
    lean = (-1, 0, 1, 0)[frame % 4] if facing in (1, 2) else 0
    cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    fitted = mod.fit_cell(img, CELL, CELL)
    cell.paste(fitted, (lean, bob), fitted)
    return cell


def build_sheet(src_name: str, out_name: str) -> None:
    path = os.path.join(SRC_DIR, src_name)
    keyed = strip_magenta(mod.flood_key(Image.open(path).convert("RGBA"), threshold=56))
    walk = Image.new("RGBA", (FRAMES * CELL, DIRS * CELL), (0, 0, 0, 0))
    for d in range(DIRS):
        for f in range(FRAMES):
            walk.paste(pose(keyed, d, f), (f * CELL, d * CELL))
    sheet = mod.append_attack_dirs(walk, CELL, CELL, DIRS, FRAMES, 2)
    dest = os.path.join(OUT_DIR, out_name)
    sheet.save(dest)
    print(f"wrote {dest} {sheet.size}")


def main() -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for src, out in SHEETS:
        build_sheet(src, out)


if __name__ == "__main__":
    main()
