"""Ensure sprite sheets have attack rows. Drop replacement PNGs then re-run."""
from __future__ import annotations

import os
from PIL import Image

SPRITES = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets\sprites"


def lunge(cell: Image.Image, dx: int, dy: int) -> Image.Image:
    out = Image.new("RGBA", cell.size, (0, 0, 0, 0))
    out.paste(cell, (dx, dy), cell)
    return out


def expand_dirs(src: Image.Image, cw: int, ch: int, dirs: int, walk_frames: int, attack_frames: int = 2) -> Image.Image:
    out = Image.new("RGBA", (walk_frames * cw, dirs * 2 * ch), (0, 0, 0, 0))
    out.paste(src.crop((0, 0, walk_frames * cw, dirs * ch)), (0, 0))
    lunge_by_dir = {0: (0, 3), 1: (-3, 0), 2: (3, 0), 3: (0, -3)}
    for d in range(dirs):
        base = src.crop((min(1, walk_frames - 1) * cw, d * ch, min(1, walk_frames - 1) * cw + cw, d * ch + ch))
        dx, dy = lunge_by_dir.get(d, (0, 3))
        for f in range(attack_frames):
            framed = lunge(base, dx * (f + 1), dy * (f + 1))
            out.paste(framed, (f * cw, (dirs + d) * ch), framed)
    return out


def expand_strip(src: Image.Image, cw: int, ch: int, walk_frames: int, attack_frames: int = 2) -> Image.Image:
    out = Image.new("RGBA", (walk_frames * cw, ch * 2), (0, 0, 0, 0))
    out.paste(src.crop((0, 0, walk_frames * cw, ch)), (0, 0))
    for f in range(attack_frames):
        col = min(f, walk_frames - 1)
        base = src.crop((col * cw, 0, col * cw + cw, ch))
        framed = lunge(base, 0, 3 + f * 2)
        out.paste(framed, (f * cw, ch), framed)
    return out


def main() -> None:
    os.makedirs(SPRITES, exist_ok=True)

    for name in ("player_a.png", "player_b.png", "player_c.png"):
        path = os.path.join(SPRITES, name)
        if not os.path.exists(path):
            continue
        img = Image.open(path).convert("RGBA")
        if img.size[1] <= 128:
            expand_dirs(img, 32, 32, 4, 4, 2).save(path)

    strips = [
        ("gronk.png", 48, 48, 4),
        ("cultist_acolyte.png", 32, 32, 4),
        ("cultist_torch.png", 32, 32, 4),
        ("cultist_dagger.png", 32, 32, 4),
        ("cultist_shotgun.png", 32, 32, 4),
        ("cultist_lightning.png", 32, 32, 4),
        ("cultist_chanter.png", 32, 32, 4),
        ("boss_warehouse.png", 48, 48, 4),
    ]
    for name, cw, ch, frames in strips:
        path = os.path.join(SPRITES, name)
        if not os.path.exists(path):
            continue
        img = Image.open(path).convert("RGBA")
        if img.size[1] <= ch:
            expand_strip(img, cw, ch, frames, 2).save(path)

    print("attack rows packed")


if __name__ == "__main__":
    main()
