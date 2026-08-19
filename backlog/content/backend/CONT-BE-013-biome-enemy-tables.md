# CONT-BE-013: Biome-specific enemy spawn tables

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | EnemySpawner.cs swamp_edge already; OVERWORLD_VISION regions |

## Summary

Each major biome gets a spawn table (gronk variants, cultists, later unique) so the Waste, swamp, forest, ash, and peaks do not feel like one brown enemy.

## Context

`EnemySpawner` already has some zone ids (`swamp_edge`, `swamp_npcs`). Expand without exploding difficulty.

## Acceptance criteria

- [ ] Tables for village outskirts, Waste, Yhtill, Dark Forest, ash Court, mountains/snow, shore.
- [ ] Elites remain rare.
- [ ] No spawns inside village house footprints or on dungeon entrance tiles.

## Out of scope

New enemy sprites (separate asset tickets if needed).

## Suggested files

- `EnemySpawner.cs`
- `EnemyAI.cs`

## Dependencies

- None.
