# Carcosa Overworld Vision (LTTP-scale+)

**Status:** Vision documented; greybox expansion after mesh dungeon slice.  
**Current map:** 200×200 tiles (`OverworldGenerator`) — fishing village, Lake Hali, mountains, dark forest, King in Yellow ruins, Pallid Shore.

## Scale target

| Reference | Size | Notes |
| --- | --- | --- |
| Zelda ALTTP Light World | ~128×128 metatiles, dense landmarks | Feel target |
| Carcosa today | 200×200 | Larger tiles, sparser POIs |
| Carcosa goal | ≥ ALTTP feel; prefer 256×256 or denser 200 | Region identity + routes + dungeon gates |

## Preserve (southern/central core)

- Fishing Village (spawn)
- Lake Hali + mist / Pallid Shore
- Northern mountains + passes (Mountain Cave portal)
- Dark Forest
- Ruins of the King in Yellow
- Aldebaran Crossing, Hyades Gate
- Portals: Warehouse, Temple of Hali, Mountain Cave

## Planned regions (cosmic horror greybox)

- Shores of Hali / mist marshes — expand lake into multi-screen water maze
- Carcosa on the horizon — distant towers / non-Euclidean approaches
- Court of the Dragon — ash flats; yellow-sign iconography
- Dim Carcosa approaches — canyon paths that “shouldn’t connect”
- Black stars / Hyades foothills — constellation landmark props
- Leng-like plateau / Yuggoth-touched wastes (original naming)
- Sunken Cyclopean quay — coastal dungeon gate
- Interior mountain massif — traversable caves/passes
- Meditation Altars — Offer to the Flame POIs

## Pipeline

- Ship `Assets/overworld-v{major}.json` for P2P (`StaticOverworldAsset`)
- Long-term: `Carcosa.Server` owns overworld data; matchmaking is tracker-only
