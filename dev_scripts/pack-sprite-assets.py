"""Pack generated Carcosa art into 32px tileset atlases and entity spritesheets.

Art swap: drop source PNGs named player_a/b/c, gronk, cultist_*, boss_warehouse,
tree, etc. into SRC, then run this script. In-game ids are keys in
src/frontend/public/assets/sprites/manifest.json (`file` lets several ids share
one sheet). Walk rows come first; attack rows are appended automatically.
"""
from __future__ import annotations

import math
import os
import random
from collections import deque

from PIL import Image, ImageEnhance, ImageFilter

SRC = r"C:\Users\Brandon\.cursor\projects\c-Users-Brandon-VS-Code-Projects-carcosa-bbq\assets"
OUT_TILES = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\tilesets"
OUT_SPRITES = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\sprites"
TILE = 32
ATLAS = 8


def load(name: str) -> Image.Image:
    return Image.open(os.path.join(SRC, name)).convert("RGBA")


def clamp(v: int) -> int:
    return 0 if v < 0 else 255 if v > 255 else v


def lerp(a: int, b: int, t: float) -> int:
    return int(a + (b - a) * t)


def sample_patch(src: Image.Image, col: int, row: int, cols: int, rows: int, jitter: int, rng: random.Random) -> Image.Image:
    w, h = src.size
    cw, ch = max(1, w // cols), max(1, h // rows)
    x = min(w - cw, max(0, col * cw + rng.randint(-jitter, jitter)))
    y = min(h - ch, max(0, row * ch + rng.randint(-jitter, jitter)))
    crop = src.crop((x, y, x + cw, y + ch))
    return crop.resize((TILE, TILE), Image.Resampling.BOX)


def overlay_pixel(px, color, alpha: float):
    r, g, b, a = px
    return (
        clamp(int(r * (1 - alpha) + color[0] * alpha)),
        clamp(int(g * (1 - alpha) + color[1] * alpha)),
        clamp(int(b * (1 - alpha) + color[2] * alpha)),
        a,
    )


def dither(tile: Image.Image, amount: int, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            r, g, b, a = pix[x, y]
            n = rng.randint(-amount, amount)
            pix[x, y] = (clamp(r + n), clamp(g + n), clamp(b + n), a)
    return tile


def pattern_grass(tile: Image.Image, rng: random.Random, dark: bool = False) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if rng.random() < 0.18:
                shade = rng.choice([(18, 28, 12), (42, 62, 28), (8, 14, 8)] if not dark else [(8, 14, 8), (20, 32, 16), (4, 8, 4)])
                pix[x, y] = overlay_pixel(pix[x, y], shade, 0.45)
            if rng.random() < 0.04:
                pix[x, y] = overlay_pixel(pix[x, y], (90, 70, 30), 0.3)
    return tile


def pattern_water(tile: Image.Image, frame: int, shallow: bool = False) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            wave = math.sin((x + frame * 3) * 0.4 + y * 0.15) * 0.5 + 0.5
            foam = 1.0 if (y + frame) % 8 == (x // 3) % 8 else 0.0
            color = (20, 70, 90) if shallow else (8, 22, 48)
            highlight = (80, 180, 190) if shallow else (40, 140, 160)
            mix = color if wave < 0.65 else highlight
            pix[x, y] = overlay_pixel(pix[x, y], mix, 0.28 + foam * 0.2)
    return tile


def pattern_sand(tile: Image.Image, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if rng.random() < 0.2:
                pix[x, y] = overlay_pixel(pix[x, y], (122, 106, 74), 0.35)
            if rng.random() < 0.05:
                pix[x, y] = overlay_pixel(pix[x, y], (60, 48, 32), 0.4)
    return tile


def pattern_mist(tile: Image.Image, frame: int) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            m = (math.sin((x + frame * 2) * 0.2) + math.cos((y - frame) * 0.25)) * 0.25 + 0.5
            pix[x, y] = overlay_pixel(pix[x, y], (140, 160, 168), 0.15 + m * 0.25)
    return tile


def pattern_planks(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if y % 8 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (20, 12, 6), 0.55)
            elif x % 16 == (y // 8) % 2:
                pix[x, y] = overlay_pixel(pix[x, y], (40, 28, 14), 0.25)
    return tile


def pattern_cobble(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if x % 8 == 0 or y % 8 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (18, 16, 14), 0.55)
            elif (x // 8 + y // 8) % 2 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (70, 68, 62), 0.15)
    return tile


def pattern_path(tile: Image.Image, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if abs(x - 16) < 2 or rng.random() < 0.08:
                pix[x, y] = overlay_pixel(pix[x, y], (40, 30, 16), 0.35)
    return tile


def pattern_ruins(tile: Image.Image, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if x % 10 == 0 or y % 12 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (12, 10, 8), 0.5)
            if rng.random() < 0.08:
                pix[x, y] = overlay_pixel(pix[x, y], (90, 40, 30), 0.3)
    return tile


def pattern_floor(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if abs(x - 16) + abs(y - 16) in (4, 8, 12, 16):
                pix[x, y] = overlay_pixel(pix[x, y], (201, 168, 76), 0.4)
            if x == 0 or y == 0 or x == 31 or y == 31:
                pix[x, y] = overlay_pixel(pix[x, y], (40, 20, 10), 0.35)
    return tile


def pattern_wall(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if x % 5 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (80, 10, 14), 0.4)
            if y % 11 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (20, 4, 6), 0.45)
    return tile


def pattern_door(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            in_arch = (x - 16) ** 2 / 100 + (y - 18) ** 2 / 160 < 1 and y > 6
            if in_arch:
                pix[x, y] = overlay_pixel(pix[x, y], (20, 8, 6), 0.55)
            if x in (6, 25) or y in (6, 30):
                pix[x, y] = overlay_pixel(pix[x, y], (180, 140, 50), 0.45)
    return tile


def pattern_entrance(tile: Image.Image, frame: int) -> Image.Image:
    pix = tile.load()
    pulse = 0.35 + 0.15 * math.sin(frame)
    for y in range(TILE):
        for x in range(TILE):
            dx, dy = x - 16, y - 16
            d = math.sqrt(dx * dx + dy * dy)
            if d < 10:
                pix[x, y] = overlay_pixel(pix[x, y], (10, 0, 0), 0.7)
            elif d < 13:
                pix[x, y] = overlay_pixel(pix[x, y], (201, 168, 76), pulse)
    return tile


def pattern_forest(tile: Image.Image, rng: random.Random) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            if rng.random() < 0.12:
                pix[x, y] = overlay_pixel(pix[x, y], (8, 18, 8), 0.6)
            if (x + y * 3) % 11 == 0:
                pix[x, y] = overlay_pixel(pix[x, y], (4, 8, 4), 0.5)
    return tile


def pattern_mountain(tile: Image.Image) -> Image.Image:
    pix = tile.load()
    for y in range(TILE):
        for x in range(TILE):
            ridge = 1 if (x + y // 2) % 9 < 2 else 0
            pix[x, y] = overlay_pixel(pix[x, y], (90, 80, 70) if ridge else (30, 30, 36), 0.28)
    return tile


def build_atlas(src: Image.Image, kinds: list[tuple[str, dict]]) -> Image.Image:
    atlas = Image.new("RGBA", (TILE * ATLAS, TILE * ATLAS), (0, 0, 0, 255))
    for i, (kind, opts) in enumerate(kinds):
        col, row = i % ATLAS, i // ATLAS
        rng = random.Random(1000 + i * 17)
        patch = sample_patch(src, col, row, ATLAS, ATLAS, opts.get("jitter", 12), rng)
        patch = ImageEnhance.Color(patch).enhance(0.85)
        patch = ImageEnhance.Contrast(patch).enhance(1.15)
        frame = opts.get("frame", 0)
        if kind == "grass":
            patch = pattern_grass(patch, rng, dark=False)
        elif kind == "darkgrass":
            patch = pattern_grass(patch, rng, dark=True)
        elif kind == "water":
            patch = pattern_water(patch, frame, shallow=False)
        elif kind == "shallow":
            patch = pattern_water(patch, frame, shallow=True)
        elif kind == "sand":
            patch = pattern_sand(patch, rng)
        elif kind == "mist":
            patch = pattern_mist(patch, frame)
        elif kind == "bridge":
            patch = pattern_planks(patch)
        elif kind == "path":
            patch = pattern_path(patch, rng)
        elif kind == "cobble":
            patch = pattern_cobble(patch)
        elif kind == "ruins":
            patch = pattern_ruins(patch, rng)
        elif kind == "floor":
            patch = pattern_floor(patch)
        elif kind == "wall":
            patch = pattern_wall(patch)
        elif kind == "door":
            patch = pattern_door(patch)
        elif kind == "entrance":
            patch = pattern_entrance(patch, frame)
        elif kind == "forest":
            patch = pattern_forest(patch, rng)
        elif kind == "mountain":
            patch = pattern_mountain(patch)
        patch = dither(patch, 7, rng)
        atlas.paste(patch, (col * TILE, row * TILE))
    return atlas


def repeat_kinds(spec: list[tuple[str, int, dict | None]]) -> list[tuple[str, dict]]:
    out: list[tuple[str, dict]] = []
    for kind, count, base in spec:
        base = dict(base or {})
        for i in range(count):
            opts = dict(base)
            if kind in ("water", "shallow", "mist", "entrance"):
                opts["frame"] = i % 4
            out.append((kind, opts))
    while len(out) < 64:
        out.append((spec[-1][0], dict(spec[-1][2] or {})))
    return out[:64]


def color_dist(a, b) -> float:
    return math.sqrt(sum((int(a[i]) - int(b[i])) ** 2 for i in range(3)))


def is_magenta(p) -> bool:
    r, g, b = p[:3]
    return r > 180 and b > 180 and g < 120


def flood_key(img: Image.Image, threshold: float = 48) -> Image.Image:
    img = img.convert("RGBA")
    w, h = img.size
    pix = img.load()
    corners = [pix[0, 0], pix[w - 1, 0], pix[0, h - 1], pix[w - 1, h - 1]]
    bg = tuple(sum(c[i] for c in corners) // 4 for i in range(3))
    use_magenta = sum(1 for c in corners if is_magenta(c)) >= 2

    visited = [[False] * h for _ in range(w)]
    q: deque[tuple[int, int]] = deque()
    for x in range(w):
        q.append((x, 0))
        q.append((x, h - 1))
    for y in range(h):
        q.append((0, y))
        q.append((w - 1, y))

    while q:
        x, y = q.popleft()
        if x < 0 or y < 0 or x >= w or y >= h or visited[x][y]:
            continue
        visited[x][y] = True
        p = pix[x, y]
        keyed = is_magenta(p) if use_magenta else color_dist(p, bg) < threshold
        if not keyed:
            continue
        pix[x, y] = (p[0], p[1], p[2], 0)
        q.extend(((x + 1, y), (x - 1, y), (x, y + 1), (x, y - 1)))
    return img


def content_bbox(img: Image.Image, alpha_min: int = 24) -> tuple[int, int, int, int] | None:
    pix = img.load()
    w, h = img.size
    minx, miny, maxx, maxy = w, h, 0, 0
    found = False
    for y in range(h):
        for x in range(w):
            if pix[x, y][3] > alpha_min:
                found = True
                minx, miny = min(minx, x), min(miny, y)
                maxx, maxy = max(maxx, x), max(maxy, y)
    if not found:
        return None
    return minx, miny, maxx + 1, maxy + 1


def fit_cell(src: Image.Image, cw: int, ch: int) -> Image.Image:
    cell = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
    bbox = content_bbox(src)
    if not bbox:
        return cell
    cropped = src.crop(bbox)
    bw, bh = cropped.size
    scale = min(cw / bw, ch / bh, 1.0) if max(bw, bh) <= max(cw, ch) * 1.4 else min(cw / bw, ch / bh)
    # Always scale to fill most of the cell while keeping aspect
    scale = min((cw * 0.92) / bw, (ch * 0.92) / bh)
    nw, nh = max(1, int(bw * scale)), max(1, int(bh * scale))
    resized = cropped.resize((nw, nh), Image.Resampling.NEAREST)
    cell.paste(resized, ((cw - nw) // 2, (ch - nh) // 2), resized)
    return cell


def pack_grid(src: Image.Image, cols: int, rows: int, cw: int, ch: int) -> Image.Image:
    keyed = flood_key(src)
    w, h = keyed.size
    out = Image.new("RGBA", (cols * cw, rows * ch), (0, 0, 0, 0))
    cell_w, cell_h = w / cols, h / rows
    for r in range(rows):
        for c in range(cols):
            box = (
                int(c * cell_w),
                int(r * cell_h),
                int((c + 1) * cell_w),
                int((r + 1) * cell_h),
            )
            out.paste(fit_cell(keyed.crop(box), cw, ch), (c * cw, r * ch))
    return out


def pack_strip(src: Image.Image, frames: int, cw: int, ch: int, rows: int = 1) -> Image.Image:
    keyed = flood_key(src)
    w, h = keyed.size
    if rows > 1:
        return pack_grid(src, frames, rows, cw, ch)
    out = Image.new("RGBA", (frames * cw, ch), (0, 0, 0, 0))
    fw = w / frames
    for i in range(frames):
        box = (int(i * fw), 0, int((i + 1) * fw), h)
        out.paste(fit_cell(keyed.crop(box), cw, ch), (i * cw, 0))
    return out


def pack_single(src: Image.Image, cw: int, ch: int) -> Image.Image:
    return fit_cell(flood_key(src), cw, ch)


def append_attack_dirs(src: Image.Image, cw: int, ch: int, dirs: int, walk_frames: int, attack_frames: int = 2) -> Image.Image:
    out = Image.new("RGBA", (walk_frames * cw, dirs * 2 * ch), (0, 0, 0, 0))
    out.paste(src.crop((0, 0, walk_frames * cw, dirs * ch)), (0, 0))
    lunge_by_dir = {0: (0, 3), 1: (-3, 0), 2: (3, 0), 3: (0, -3)}
    for d in range(dirs):
        col = min(1, walk_frames - 1)
        base = src.crop((col * cw, d * ch, col * cw + cw, d * ch + ch))
        dx, dy = lunge_by_dir.get(d, (0, 3))
        for f in range(attack_frames):
            framed = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
            framed.paste(base, (dx * (f + 1), dy * (f + 1)), base)
            out.paste(framed, (f * cw, (dirs + d) * ch), framed)
    return out


def append_attack_strip(src: Image.Image, cw: int, ch: int, walk_frames: int, attack_frames: int = 2) -> Image.Image:
    out = Image.new("RGBA", (walk_frames * cw, ch * 2), (0, 0, 0, 0))
    out.paste(src.crop((0, 0, walk_frames * cw, ch)), (0, 0))
    for f in range(attack_frames):
        col = min(f, walk_frames - 1)
        base = src.crop((col * cw, 0, col * cw + cw, ch))
        framed = Image.new("RGBA", (cw, ch), (0, 0, 0, 0))
        framed.paste(base, (0, 3 + f * 2), base)
        out.paste(framed, (f * cw, ch), framed)
    return out


def main() -> None:
    os.makedirs(OUT_TILES, exist_ok=True)
    os.makedirs(OUT_SPRITES, exist_ok=True)

    shore = load("shore_of_hali.png")
    palaces = load("yellow_palaces.png")
    ruined = load("ruined_carcosa.png")
    waste = load("the_waste.png")

    shore_atlas = build_atlas(shore, repeat_kinds([
        ("water", 8, None),
        ("shallow", 8, None),
        ("sand", 8, None),
        ("mist", 8, None),
        ("bridge", 8, None),
        ("sand", 8, None),
        ("shallow", 8, None),
        ("water", 8, None),
    ]))
    palace_atlas = build_atlas(palaces, repeat_kinds([
        ("floor", 8, None),
        ("wall", 8, None),
        ("door", 4, None),
        ("entrance", 4, None),
        ("floor", 8, None),
        ("wall", 8, None),
        ("floor", 8, None),
        ("door", 8, None),
        ("entrance", 8, None),
    ]))
    ruin_atlas = build_atlas(ruined, repeat_kinds([
        ("path", 8, None),
        ("cobble", 8, None),
        ("ruins", 8, None),
        ("cobble", 8, None),
        ("path", 8, None),
        ("ruins", 8, None),
        ("cobble", 8, None),
        ("path", 8, None),
    ]))
    waste_atlas = build_atlas(waste, repeat_kinds([
        ("grass", 8, None),
        ("darkgrass", 8, None),
        ("forest", 8, None),
        ("mountain", 8, None),
        ("grass", 8, None),
        ("darkgrass", 8, None),
        ("forest", 8, None),
        ("mountain", 8, None),
    ]))

    shore_atlas.save(os.path.join(OUT_TILES, "shore_of_hali.png"))
    palace_atlas.save(os.path.join(OUT_TILES, "yellow_palaces.png"))
    ruin_atlas.save(os.path.join(OUT_TILES, "ruined_carcosa.png"))
    waste_atlas.save(os.path.join(OUT_TILES, "the_waste.png"))

    pack_grid(load("player_a.png"), 4, 4, 32, 32).save(os.path.join(OUT_SPRITES, "player_a.png"))
    pack_grid(load("player_b.png"), 4, 4, 32, 32).save(os.path.join(OUT_SPRITES, "player_b.png"))
    pack_grid(load("player_c.png"), 4, 4, 32, 32).save(os.path.join(OUT_SPRITES, "player_c.png"))
    pack_strip(load("gronk.png"), 4, 48, 48).save(os.path.join(OUT_SPRITES, "gronk.png"))
    pack_single(load("tree.png"), 32, 48).save(os.path.join(OUT_SPRITES, "tree.png"))
    pack_single(load("ruined_pillar.png"), 24, 40).save(os.path.join(OUT_SPRITES, "ruined_pillar.png"))
    pack_single(load("fishing_boat.png"), 40, 24).save(os.path.join(OUT_SPRITES, "fishing_boat.png"))
    pack_single(load("signpost.png"), 16, 32).save(os.path.join(OUT_SPRITES, "signpost.png"))
    pack_single(load("house.png"), 64, 64).save(os.path.join(OUT_SPRITES, "house.png"))
    pack_strip(load("dungeon_entrance.png"), 2, 32, 32).save(os.path.join(OUT_SPRITES, "dungeon_entrance.png"))
    for name in ("cultist_acolyte", "cultist_torch", "cultist_dagger", "cultist_shotgun", "cultist_lightning", "cultist_chanter"):
        pack_strip(load(f"{name}.png"), 4, 32, 32).save(os.path.join(OUT_SPRITES, f"{name}.png"))
    pack_grid(load("boss_warehouse.png"), 2, 2, 48, 48).save(os.path.join(OUT_SPRITES, "boss_warehouse.png"))
    # Flatten boss 2x2 into a 4-frame strip
    boss = Image.open(os.path.join(OUT_SPRITES, "boss_warehouse.png"))
    strip = Image.new("RGBA", (192, 48), (0, 0, 0, 0))
    for i, (c, r) in enumerate(((0, 0), (1, 0), (0, 1), (1, 1))):
        strip.paste(boss.crop((c * 48, r * 48, c * 48 + 48, r * 48 + 48)), (i * 48, 0))
    strip.save(os.path.join(OUT_SPRITES, "boss_warehouse.png"))

    for letter in ("a", "b", "c"):
        path = os.path.join(OUT_SPRITES, f"player_{letter}.png")
        img = Image.open(path).convert("RGBA")
        append_attack_dirs(img, 32, 32, 4, 4).save(path)
    for name, cw, ch, frames in (
        ("gronk.png", 48, 48, 4),
        ("cultist_acolyte.png", 32, 32, 4),
        ("cultist_torch.png", 32, 32, 4),
        ("cultist_dagger.png", 32, 32, 4),
        ("cultist_shotgun.png", 32, 32, 4),
        ("cultist_lightning.png", 32, 32, 4),
        ("cultist_chanter.png", 32, 32, 4),
        ("boss_warehouse.png", 48, 48, 4),
    ):
        path = os.path.join(OUT_SPRITES, name)
        img = Image.open(path).convert("RGBA")
        append_attack_strip(img, cw, ch, frames).save(path)
    print("packed tilesets + sprites")


if __name__ == "__main__":
    main()
