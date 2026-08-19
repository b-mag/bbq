# CONT-BE-015: Dark Forest paths and encounters

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OVERWORLD_VISION.md Dark Forest winding paths |

## Summary

East Dark Forest should have winding walkable paths through trees, ranger NPC, and forest-specific spawns — not a solid tree stamp.

## Context

Trees are rng-scattered on grass/darkgrass/swamp globally (420). Forest region may be too dense or too empty.

## Acceptance criteria

- [ ] Winding path tiles through the forest landmark.
- [ ] Spawn table distinct from swamp.
- [ ] Ranger remains talkable.

## Out of scope

Lost-woods teleport maze.

## Suggested files

- `OverworldWorldGen.cs`
- `EnemySpawner.cs`

## Dependencies

- CONT-BE-013
