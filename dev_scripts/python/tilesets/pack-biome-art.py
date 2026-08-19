"""Slice raw_assets tilesheets into biome atlas + prop sprites, and stamp slash placeholders.

Source JPGs are labeled concept sheets (not grid-perfect). We sample regions,
pixelate them, and emit assets the game can swap later by dropping replacements
in public/assets/sprites and public/assets/tilesets.
"""
from __future__ import annotations

import math
import os
import random

from PIL import Image, ImageDraw, ImageEnhance, ImageFilter

ROOT = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq"
RAW = os.path.join(ROOT, "raw_assets")
OUT_TILES = os.path.join(ROOT, "src", "frontend", "public", "assets", "tilesets")
OUT_SPRITES = os.path.join(ROOT, "src", "frontend", "public", "assets", "sprites")
TILE = 32


def clamp(v: int) -> int:
    return 0 if v < 0 else 255 if v > 255 else v


def load_jpg(name: str) -> Image.Image:
    return Image.open(os.path.join(RAW, name)).convert("RGB")


def pixelate(src: Image.Image, size: int = TILE) -> Image.Image:
    small = src.resize((max(8, size // 2), max(8, size // 2)), Image.Resampling.BOX)
    return small.resize((size, size), Image.Resampling.NEAREST)


def region(img: Image.Image, fx0: float, fy0: float, fx1: float, fy1: float) -> Image.Image:
    w, h = img.size
    x0, y0 = int(w * fx0), int(h * fy0)
    x1, y1 = int(w * fx1), int(h * fy1)
    x1, y1 = max(x0 + 8, x1), max(y0 + 8, y1)
    return img.crop((x0, y0, x1, y1))


def dither(tile: Image.Image, amount: int, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(tile.size[1]):
        for x in range(tile.size[0]):
            p = pix[x, y]
            n = rng.randint(-amount, amount)
            if len(p) == 4:
                pix[x, y] = (clamp(p[0] + n), clamp(p[1] + n), clamp(p[2] + n), p[3])
            else:
                pix[x, y] = (clamp(p[0] + n), clamp(p[1] + n), clamp(p[2] + n))
    return tile


def tint(tile: Image.Image, color: tuple[int, int, int], alpha: float) -> Image.Image:
    overlay = Image.new("RGB", tile.size, color)
    return Image.blend(tile.convert("RGB"), overlay, alpha)


def sample_variants(src: Image.Image, count: int, seed: int) -> list[Image.Image]:
    rng = random.Random(seed)
    w, h = src.size
    out = []
    cw, ch = max(24, w // 4), max(24, h // 4)
    for i in range(count):
        x = rng.randint(0, max(0, w - cw))
        y = rng.randint(0, max(0, h - ch))
        patch = src.crop((x, y, x + cw, y + ch))
        tile = pixelate(patch)
        tile = ImageEnhance.Color(tile).enhance(0.75)
        tile = ImageEnhance.Contrast(tile).enhance(1.2)
        tile = dither(tile, 8, random.Random(seed + i * 13))
        out.append(tile)
    return out


def key_dark(img: Image.Image, thresh: int = 28) -> Image.Image:
    img = img.convert("RGBA")
    pix = img.load()
    w, h = img.size
    for y in range(h):
        for x in range(w):
            r, g, b, a = pix[x, y]
            if r < thresh and g < thresh and b < thresh:
                pix[x, y] = (r, g, b, 0)
    return img


def fit_prop(src: Image.Image, cw: int, ch: int) -> Image.Image:
    keyed = key_dark(pixelate(src, max(cw, ch)))
    bbox = keyed.getbbox()
    cell = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    if not bbox:
        return cell
    cropped = keyed.crop(bbox)
    bw, bh = cropped.size
    scale = min((cw * 0.94) / bw, (ch * 0.94) / bh)
    nw, nh = max(1, int(bw * scale)), max(1, int(bh * scale))
    resized = cropped.resize((nw, nh), Image.Resampling.NEAREST)
    cell.paste(resized, ((cw - nw) // 2, (ch - nh) // 2), resized)
    return cell


def stamp_slash(cell: Image.Image, facing: int) -> Image.Image:
    """Placeholder melee arc: pale crescent in the facing direction."""
    out = cell.copy()
    draw = ImageDraw.Draw(out, "RGBA")
    w, h = out.size
    cx, cy = w // 2, h // 2 + 2
    if facing == 0:  # down
        box = [cx - 12, cy - 2, cx + 12, cy + 14]
        start, end = 20, 160
    elif facing == 1:  # left
        box = [cx - 16, cy - 12, cx + 2, cy + 12]
        start, end = 110, 250
    elif facing == 2:  # right
        box = [cx - 2, cy - 12, cx + 16, cy + 12]
        start, end = 290, 70
    else:  # up
        box = [cx - 12, cy - 16, cx + 12, cy + 2]
        start, end = 200, 340
    draw.arc(box, start=start, end=end, fill=(230, 220, 200, 210), width=2)
    draw.arc([b + 1 if i % 2 == 0 else b - 1 for i, b in enumerate(box)], start=start, end=end, fill=(255, 240, 180, 120), width=1)
    return out


def stamp_player_slashes() -> None:
    for letter in ("a", "b", "c"):
        path = os.path.join(OUT_SPRITES, f"player_{letter}.png")
        if not os.path.exists(path):
            continue
        img = Image.open(path).convert("RGBA")
        cw, ch = 32, 32
        dirs, frames = 4, 4
        if img.size != (frames * cw, dirs * 2 * ch):
            continue
        for d in range(dirs):
            for f in range(2):
                box = (f * cw, (dirs + d) * ch, f * cw + cw, (dirs + d) * ch + ch)
                cell = img.crop(box)
                bright = sum(1 for p in cell.getdata() if p[0] > 200 and p[3] > 100)
                if bright > 10:
                    continue
                img.paste(stamp_slash(cell, d), box)
        img.save(path)
        print("stamped slash", path)


def build_realms_atlas() -> Image.Image:
    ruined = load_jpg("EPeVf.jpg")
    organic = load_jpg("VDIzw.jpg")
    shore = load_jpg("2Mw4x.jpg")

    specs = [
        ("desert", sample_variants(region(ruined, 0.02, 0.08, 0.38, 0.34), 8, 11)),
        ("swamp", sample_variants(region(shore, 0.02, 0.48, 0.55, 0.78), 8, 22)),
        ("flesh", sample_variants(region(ruined, 0.18, 0.55, 0.62, 0.82), 8, 33)),
        ("ash", sample_variants(region(organic, 0.28, 0.38, 0.72, 0.58), 8, 44)),
        ("snow", [tint(t, (180, 190, 210), 0.35) for t in sample_variants(region(organic, 0.55, 0.08, 0.98, 0.28), 8, 55)]),
        ("palace", sample_variants(region(organic, 0.28, 0.55, 0.78, 0.72), 8, 66)),
        ("path_mtn", sample_variants(region(ruined, 0.02, 0.35, 0.42, 0.55), 8, 77)),
        ("organic_ground", sample_variants(region(organic, 0.02, 0.38, 0.45, 0.62), 8, 88)),
    ]
    atlas = Image.new("RGBA", (TILE * 8, TILE * 8), (0, 0, 0, 255))
    for row, (_name, tiles) in enumerate(specs):
        for col, tile in enumerate(tiles[:8]):
            atlas.paste(tile.convert("RGBA"), (col * TILE, row * TILE))
    return atlas


def draw_organic_house(cw: int, ch: int, seed: int, kind: str) -> Image.Image:
    """Giger/mud village props drawn as sprites (JPG sheets are labeled concept art)."""
    rng = random.Random(seed)
    img = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    mud = [(72, 48, 36), (48, 32, 24), (90, 58, 42), (30, 22, 18)]
    bone = [(180, 170, 150), (120, 110, 96), (90, 82, 70)]
    vein = [(90, 20, 28), (50, 8, 14)]
    cx = cw // 2

    if kind in ("organic_house", "giger_house", "mud_hut"):
        roof_h = ch // 2
        for y in range(ch - 6, 10, -1):
            t = (ch - 6 - y) / max(1, ch - 16)
            half = int((1 - t * 0.55) * (cw * 0.38 + rng.randint(-2, 2)))
            if kind == "giger_house":
                half = int(half * (1.0 + 0.15 * math.sin(y * 0.4)))
            y_col = mud[0] if y > roof_h else mud[1]
            if kind == "giger_house":
                y_col = (y_col[0] + 10, max(0, y_col[1] - 8), y_col[2] + 8)
            d.ellipse((cx - half, y - 3, cx + half, y + 3), fill=y_col)
        for i in range(3):
            yy = 18 + i * (ch // 6)
            d.arc((cx - cw // 3, yy, cx + cw // 3, yy + 18), 200, 340, fill=bone[1], width=2)
        d.ellipse((cx - 6, ch - 22, cx + 6, ch - 6), fill=(12, 8, 8, 255))
        d.arc((cx - 8, ch - 24, cx + 8, ch - 6), 200, 340, fill=vein[0], width=1)
        for side in (-1, 1):
            x, y = cx + side * (cw // 3), 16
            for _ in range(10):
                x += side * rng.randint(0, 2)
                y += rng.randint(1, 3)
                d.ellipse((x - 2, y - 2, x + 2, y + 2), fill=mud[2] if rng.random() < 0.5 else vein[1])
    elif kind == "dark_tower":
        for y in range(ch - 4, 4, -1):
            t = 1 - y / ch
            half = int(6 + t * 10 + 2 * math.sin(y * 0.35))
            col = (28 + int(t * 20), 24, 32 + int(t * 30))
            d.rectangle((cx - half, y, cx + half, y), fill=col)
        d.polygon([(cx, 2), (cx - 6, 14), (cx + 6, 14)], fill=(40, 36, 48))
        for yy in (ch // 3, ch // 2, 2 * ch // 3):
            d.arc((cx - 10, yy, cx + 10, yy + 12), 0, 180, fill=bone[2], width=2)
        d.rectangle((cx - 3, ch - 18, cx + 3, ch - 4), fill=(8, 6, 10, 255))
    elif kind == "bone_spire":
        x, y = cx, ch - 4
        for i in range(ch - 8):
            x += int(1.4 * math.sin(i * 0.18))
            col = bone[i % 3]
            d.ellipse((x - 4, y - i - 3, x + 4, y - i + 3), fill=col)
            if i % 7 == 0:
                d.line((x, y - i, x + rng.choice([-8, 8]), y - i - 6), fill=bone[0], width=2)
    elif kind == "wreck_boat":
        d.polygon([(6, 20), (cw - 6, 18), (cw - 10, 28), (10, 30)], fill=(70, 52, 36))
        d.line((8, 22, cw - 8, 20), fill=(30, 22, 14), width=1)
        d.line((cx, 8, cx, 22), fill=(90, 70, 50), width=2)
        d.polygon([(cx, 8), (cx + 10, 16), (cx, 16)], fill=(120, 100, 70))
    elif kind == "dock_post":
        d.rectangle((cw // 2 - 3, 8, cw // 2 + 2, ch - 2), fill=(90, 70, 48))
        d.ellipse((4, 4, cw - 4, 16), fill=(70, 54, 38))
        d.line((2, 10, cw - 2, 10), fill=(40, 28, 18), width=1)
    else:
        for i in range(0, cw, 4):
            d.line((i, 2, i + 6, ch - 2), fill=(180, 170, 150, 180), width=1)
        for j in range(0, ch, 5):
            d.line((2, j, cw - 2, j + 2), fill=(160, 150, 130, 160), width=1)

    return dither(img, 6, rng)


def extract_props() -> None:
    os.makedirs(OUT_SPRITES, exist_ok=True)
    specs = [
        ("organic_house", 64, 80, "organic_house", 101),
        ("mud_hut", 64, 64, "mud_hut", 202),
        ("giger_house", 56, 88, "giger_house", 303),
        ("dark_tower", 48, 96, "dark_tower", 404),
        ("bone_spire", 40, 80, "bone_spire", 505),
        ("wreck_boat", 48, 32, "wreck_boat", 606),
        ("dock_post", 24, 48, "dock_post", 707),
        ("village_net", 32, 32, "village_net", 808),
    ]
    for name, cw, ch, kind, seed in specs:
        draw_organic_house(cw, ch, seed, kind).save(os.path.join(OUT_SPRITES, f"{name}.png"))
        print("prop", name, cw, ch)


def main() -> None:
    os.makedirs(OUT_TILES, exist_ok=True)
    atlas = build_realms_atlas()
    atlas.save(os.path.join(OUT_TILES, "carcosa_realms.png"))
    print("wrote carcosa_realms.png", atlas.size)
    extract_props()
    stamp_player_slashes()


if __name__ == "__main__":
    main()
