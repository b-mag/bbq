# CONT-BE-006: Northern snow / ice biome content

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OVERWORLD_VISION.md Black Stars / snow peaks; user: snow/ice region |

## Summary

Treat northern snow as a real biome: ice patches, climbable terraces, cold flavor, snow weather, unique props. Black-star sky remains the north identity.

## Context

`PaintMountains` already paints Snow on the northernmost band. Ladders exist. Hermit NPC at (0.50, 0.10). Missing: ice as a tile or overlay, props, encounters, stamina rules (ARCH-BE-024).

## Acceptance criteria

- [ ] Snow/ice area is reachable via existing ladders/paths.
- [ ] At least one authored ice field or frozen tarn (not only noise snow).
- [ ] Spawn/prop pass: ice pillars / frozen wrecks (ASSET-020).
- [ ] Landmark Black Stars still maps here.
- [ ] Works with weather snow (ARCH-BE-022) without melting the tile type.

## Out of scope

Full tileset (ASSET-004). Movement modifiers (ARCH-BE-024).

## Suggested files

- `OverworldWorldGen.cs`
- `EnemySpawner.cs`

## Dependencies

- ASSET-004
- ASSET-020
- ARCH-BE-024
