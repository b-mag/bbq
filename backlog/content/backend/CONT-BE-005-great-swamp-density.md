# CONT-BE-005: Great Swamp density and encounters

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OVERWORLD_VISION.md Marshes of Yhtill; user: great swamp biome |

## Summary

Marshes of Yhtill should feel like a Great Swamp: denser swamp tiles, pools, reed props, unique spawn table, heavier rain weight (table in CONT-BE-007).

## Context

`PaintSwamp` and landmark “Marshes of Yhtill” exist. Trees already scatter on swamp tiles. Needs more biome identity: fewer generic grass pockets, marsh NPC already placed (`npc_marsh`). Enemy table is CONT-BE-013 — this ticket is terrain + props + spawn zone id.

## Acceptance criteria

- [ ] Swamp region reads as a large marsh south of Hali, not a stain.
- [ ] Added swamp props (reeds/pools) using ASSET-005 ids or placeholders.
- [ ] Enemy spawn zone `swamp_edge` / marsh is clearly the Great Swamp.
- [ ] Walkable paths so it is not a stamina death maze (until Reed-Whistle).

## Out of scope

Weather weights (CONT-BE-007). Swamp tileset polish (ASSET-005).

## Suggested files

- `OverworldWorldGen.cs`
- `EnemySpawner.cs`

## Dependencies

- ASSET-005 preferred. CONT-BE-013 for full tables.
