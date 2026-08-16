"""Sample dominant colors from packed tilesets and sprites into palettes.json."""
from __future__ import annotations

import json
import os
from collections import Counter

from PIL import Image

ROOT = r"C:\Users\Brandon\VS Code Projects\carcosa\bbq\src\frontend\public\assets"
OUT = os.path.join(ROOT, "palettes.json")

NAMED = {
    "gold": {
        "name": "Carcosa Gold & Black Ichor",
        "use": "palaces, brass, royal stone",
        "colors": ["#1A1208", "#2E2214", "#4A3A22", "#8B6B2E", "#C9A84C", "#E8D48B", "#3D1F0F", "#6B3A28", "#A67C52"],
    },
    "flesh": {
        "name": "Giger Biomechanical Flesh",
        "use": "creatures, organic walls, wet interiors",
        "colors": ["#0D0505", "#1F0A0A", "#4A1515", "#8B2A2A", "#C45C4A", "#E8C8A0", "#A89070", "#5C3A2A", "#3A2820"],
    },
    "purple": {
        "name": "Purple Dream / Non-Euclidean Chamber",
        "use": "mist, peaks sky, alien rooms",
        "colors": ["#0F0518", "#1E0F2E", "#3A1F5C", "#6B3A9E", "#9B6BC9", "#C9A8E0", "#2A4A3A", "#5C7A4A", "#8BA87A"],
    },
    "teal": {
        "name": "Abyssal Teal & Black Water",
        "use": "lakes, sand, docks",
        "colors": ["#020810", "#0A1A22", "#1A3A4A", "#2A6A7A", "#4A9AAB", "#8AC8D8", "#1A2A28", "#3A4A42", "#6A8A7A"],
    },
    "crimson": {
        "name": "Scorched Crimson Wastes",
        "use": "desert, ash, heat",
        "colors": ["#0A0404", "#1F0A0A", "#4A1510", "#8B2A18", "#C45A30", "#E8A060", "#3A2210", "#6B4A28", "#A87A50"],
    },
    "chartreuse": {
        "name": "Sickly Chartreuse & Void",
        "use": "forest, swamp, grass",
        "colors": ["#0A0A05", "#1A1A0A", "#3A3A18", "#6B6B28", "#A8A84A", "#D8D88A", "#2A2210", "#5C4A28", "#8B7A50"],
    },
    "drowned_dock": {
        "name": "Drowned Dock / Labyrinth of Dagon",
        "use": "dungeon tiles, dream-ship, canal water",
        "colors": ["#0A0D0B", "#1A1A1A", "#2E332A", "#3D3A30", "#8B4513", "#A0522D", "#B8860B", "#D2B48C", "#E8C8A0"],
    },
    "agwan": {
        "name": "Agwan Flesh & Ichor",
        "use": "Merek, Agwan wardens",
        "colors": ["#0D0505", "#1A1208", "#3A2820", "#8B2A2A", "#A0522D", "#C45C4A", "#E8C8A0", "#E8D4B8", "#1A1A1A"],
    },
}


def quantize(r: int, g: int, b: int) -> tuple[int, int, int]:
    return (r // 16 * 16, g // 16 * 16, b // 16 * 16)


def hex_of(c: tuple[int, int, int]) -> str:
    return f"#{c[0]:02X}{c[1]:02X}{c[2]:02X}"


def sample(path: str, limit: int = 8) -> list[str]:
    img = Image.open(path).convert("RGBA")
    counts: Counter[tuple[int, int, int]] = Counter()
    px = img.load()
    w, h = img.size
    step = max(1, (w * h) // 4000)
    i = 0
    for y in range(h):
        for x in range(w):
            i += 1
            if i % step:
                continue
            r, g, b, a = px[x, y]
            if a < 40:
                continue
            if r > 170 and b > 170 and g < 140:
                continue
            counts[quantize(r, g, b)] += 1
    return [hex_of(c) for c, _ in counts.most_common(limit)]


def walk(rel: str) -> list[dict]:
    folder = os.path.join(ROOT, rel)
    rows = []
    if not os.path.isdir(folder):
        return rows
    for name in sorted(os.listdir(folder)):
        if not name.lower().endswith(".png"):
            continue
        path = os.path.join(folder, name)
        rows.append({
            "file": f"{rel}/{name}",
            "sampled": sample(path),
        })
    return rows


def main() -> None:
    doc = {
        "version": 1,
        "updated": "2026-08-15",
        "purpose": "Art reference. Named palettes are the ones to paint with. sampled[] is what is actually in each PNG right now.",
        "named": NAMED,
        "tilesets": walk("tilesets"),
        "sprites": walk("sprites"),
        "howToUse": [
            "Pick a named palette when making new art so biomes stay consistent.",
            "drowned_dock = Labyrinth of Dagon dungeon + dream-ship.",
            "agwan = Merek and entrance wardens (bone/charcoal + rust).",
            "Re-run this script after packing new PNGs: python dev_scripts/extract-palettes.py",
        ],
    }
    with open(OUT, "w", encoding="utf-8") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print(f"wrote {OUT} tilesets={len(doc['tilesets'])} sprites={len(doc['sprites'])}")


if __name__ == "__main__":
    main()
