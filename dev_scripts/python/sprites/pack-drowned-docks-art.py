"""Pack Merek / Agwan sheets, the dream-ship prop, and the Drowned Dock tileset."""
from __future__ import annotations

import importlib.util
import os
import random

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter, ImageOps

SPEC = os.path.join(os.path.dirname(__file__), "pack-sprite-assets.py")
spec = importlib.util.spec_from_file_location("pack_sprite_assets", SPEC)
mod = importlib.util.module_from_spec(spec)
assert spec.loader
spec.loader.exec_module(mod)

SRC_DIR = r"C:\Users\Brandon\.cursor\projects\c-Users-Brandon-VS-Code-Projects-carcosa-bbq\assets"
OUT_SPRITES = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\sprites"
OUT_TILES = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\tilesets"
CELL = 32
FRAMES = 4
DIRS = 4

# Drowned Dock / Labyrinth of Dagon — pulled from the industrial-organic reference.
P = {
    "void": (10, 13, 11, 255),
    "shadow": (26, 26, 26, 255),
    "olive": (46, 51, 42, 255),
    "stone": (61, 58, 48, 255),
    "rust": (139, 69, 19, 255),
    "sienna": (160, 82, 45, 255),
    "gold": (184, 134, 11, 255),
    "tan": (210, 180, 140, 255),
    "flesh": (232, 200, 160, 255),
    "blood": (90, 20, 18, 255),
}


def strip_magenta(img: Image.Image) -> Image.Image:
    pix = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = pix[x, y]
            if r > 170 and b > 170 and g < 140:
                pix[x, y] = (r, g, b, 0)
            elif r > 220 and g < 40 and b > 220:
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
    dest = os.path.join(OUT_SPRITES, out_name)
    sheet.save(dest)
    print(f"wrote {dest} {sheet.size}")


def pack_ship() -> None:
    path = os.path.join(SRC_DIR, "dream_ship_src.png")
    img = strip_magenta(mod.flood_key(Image.open(path).convert("RGBA"), threshold=48))
    # Trim transparent padding, keep a little keel margin.
    bbox = img.getbbox()
    if bbox:
        img = img.crop(bbox)
    dest_w, dest_h = 192, 112
    fitted = img.copy()
    fitted.thumbnail((dest_w, dest_h - 4), Image.Resampling.NEAREST)
    out = Image.new("RGBA", (dest_w, dest_h), (0, 0, 0, 0))
    x = (dest_w - fitted.size[0]) // 2
    y = dest_h - fitted.size[1]
    out.paste(fitted, (x, y), fitted)
    dest = os.path.join(OUT_SPRITES, "dream_ship.png")
    out.save(dest)
    print(f"wrote {dest} {out.size}")


def noise_tile(rng: random.Random, base: tuple[int, int, int, int], accents: list[tuple[int, int, int, int]]) -> Image.Image:
    img = Image.new("RGBA", (CELL, CELL), base)
    px = img.load()
    for y in range(CELL):
        for x in range(CELL):
            if rng.random() < 0.18:
                px[x, y] = accents[rng.randrange(len(accents))]
    return img


def rib_tile(rng: random.Random, base, rib, glow=None) -> Image.Image:
    img = noise_tile(rng, base, [rib, P["shadow"], base])
    draw = ImageDraw.Draw(img)
    for i in range(0, CELL, 4):
        c = glow if glow and i % 8 == 0 else rib
        draw.line([(i, 0), (i, CELL - 1)], fill=c)
    if rng.random() < 0.5:
        draw.ellipse((8, 8, 24, 24), outline=P["gold"])
    return img.filter(ImageFilter.SMOOTH_MORE)


def water_frame(rng: random.Random, phase: int) -> Image.Image:
    img = Image.new("RGBA", (CELL, CELL), P["void"])
    px = img.load()
    for y in range(CELL):
        for x in range(CELL):
            wave = (x + phase * 3 + y // 2) % 8
            if wave < 2:
                px[x, y] = P["olive"]
            elif wave < 4:
                px[x, y] = P["shadow"]
            elif rng.random() < 0.06:
                px[x, y] = P["gold"]
    return img


def dock_tile(rng: random.Random) -> Image.Image:
    img = noise_tile(rng, P["rust"], [P["sienna"], P["stone"], P["shadow"]])
    draw = ImageDraw.Draw(img)
    for y in range(2, CELL, 6):
        draw.line([(0, y), (CELL - 1, y)], fill=P["shadow"])
    return img


def sand_tile(rng: random.Random) -> Image.Image:
    return noise_tile(rng, P["sienna"], [P["tan"], P["rust"], P["stone"]])


def door_tile(rng: random.Random) -> Image.Image:
    img = rib_tile(rng, P["shadow"], P["stone"], P["gold"])
    draw = ImageDraw.Draw(img)
    draw.rectangle((6, 4, 25, 30), fill=P["void"], outline=P["gold"])
    draw.ellipse((12, 12, 20, 22), outline=P["tan"])
    return img


def build_tileset() -> None:
    rng = random.Random(0xD06A)
    cols, rows = 8, 6
    atlas = Image.new("RGBA", (cols * CELL, rows * CELL), (0, 0, 0, 0))

    makers = [
        lambda: rib_tile(rng, P["stone"], P["olive"], P["gold"]),   # floor
        lambda: rib_tile(rng, P["void"], P["olive"], P["blood"]),   # wall
        lambda: door_tile(rng),                                     # door
        None,                                                       # water frames
        lambda: dock_tile(rng),                                     # cobble / docks
        lambda: sand_tile(rng),                                     # silt
    ]

    for row, maker in enumerate(makers):
        for col in range(4):
            tile = water_frame(rng, col) if maker is None else maker()
            atlas.paste(tile, (col * CELL, row * CELL))

    dest = os.path.join(OUT_TILES, "drowned_docks.png")
    atlas.save(dest)
    print(f"wrote {dest} {atlas.size}")


def main() -> None:
    os.makedirs(OUT_SPRITES, exist_ok=True)
    os.makedirs(OUT_TILES, exist_ok=True)
    build_sheet("merek_src.png", "npc_merek.png")
    build_sheet("agwan_src.png", "npc_agwan.png")
    pack_ship()
    build_tileset()


if __name__ == "__main__":
    main()
