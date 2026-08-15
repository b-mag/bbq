# Carcosa Overworld Vision (LTTP-scale+)

**Status:** 640×640 live map in `OverworldWorldGen` (about 10× the old 200×200 area).  
**Current map:** fishing village (organic/Giger houses), Lake Hali, The Waste (desert), Marshes of Yhtill (swamp), northern climbable mountains with black-star sky, Yellow Palaces, King in Yellow ruins, Court of the Dragon ash flats, river + bridge, Dark Forest.

## Scale

| Reference | Size | Notes |
| --- | --- | --- |
| Zelda ALTTP Light World | ~256×176 tiles | Feel target |
| Carcosa greybox | 200×200 | Retired |
| Carcosa now | 640×640 | ~10× previous area; ~2.4 min walk across at 4.5 tiles/s |

## Regions (north → south)

- **Black Stars / snow peaks** — northernmost climbable paths; skybox of black stars
- **Mountain range + three passes**
- **Court of the Dragon** — ash west of the Hyades Gate
- **Yellow Palaces + Ruins of the King in Yellow** — northeast
- **The Waste** — western desert
- **Lake Hali + Pallid Shore + mist**
- **Marshes of Yhtill** — Dark World swamp south of the lake
- **Dark Forest** — east, with winding paths
- **Aldebaran Crossing** — central crossroads, river bridge on the north-south road
- **Fishing Village** — south-central spawn, organic mud houses
- **West Hamlet** — decrepit coastal hamlet
- **Shore of Hali** — southern ocean, docks, wrecks

## Art pipeline

- Terrain: `/assets/tilesets/manifest.json` + PNG sheets (including `carcosa_realms.png`)
- Entities: `/assets/sprites/manifest.json` — drop a PNG, add/edit a key. `file` lets several ids share one sheet.
- Walk rows first, optional attack rows below. Character select shows frame 0 of the down-facing walk row at 2×.
- Full art reference (facing, anchors, unique map placements): `/assets/catalog.json` (`src/frontend/public/assets/catalog.json`). Update it whenever you add or replace a PNG.

## Preserve

- Portals: Drowned Dock, Temple of Hali, Mountain Cave, plus Sunken Quay and Palace Crypt
- Twin Suns Road (north-south), east-west waste-to-forest road
