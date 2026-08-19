# Python tooling

Asset pipeline scripts. These do not launch the game. Run them from a machine with Python 3 and Pillow (`pip install pillow`).

## palettes/

| Script | What it does |
| --- | --- |
| `extract-palettes.py` | Samples dominant colors from packed tilesets/sprites into `src/frontend/public/assets/palettes.json`. |

## sprites/

| Script | What it does |
| --- | --- |
| `pack-sprite-assets.py` | Packs source PNGs into 32px tileset atlases and entity spritesheets. Shared helpers used by the other pack scripts in this folder. |
| `pack-attack-rows.py` | Appends attack (lunge) rows to character sheets that only have walk rows. |
| `pack-villager-sprites.py` | Builds villager / satyr character sheets from portrait sources. |
| `pack-drowned-docks-art.py` | Packs Merek / Agwan sheets, the dream-ship prop, and the Drowned Dock tileset. |

## tilesets/

| Script | What it does |
| --- | --- |
| `pack-biome-art.py` | Slices concept sheets in `raw_assets/` into biome atlas tiles and prop sprites. |
