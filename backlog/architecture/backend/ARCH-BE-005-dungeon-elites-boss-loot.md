# ARCH-BE-005: Instanced dungeon trash, elites, boss, loot

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D5 + loot table |

## Summary

Once a dungeon loads, pack the route with level-appropriate trash, elite rooms, a boss in front of the exit, and drop tables that match the loot plan.

## Context

Overworld elite drops exist in `OverworldCombatSync`; dungeon loot is unused because instances never loaded. Rules: seed rolled at enter; enemies scale to average party level; elites use `elite_*` subtype; boss is one, at the end; boss loot is per party member Rare+.

## Acceptance criteria

- [ ] Trash packs along the route, HP/damage from instance `AvgLevel`.
- [ ] Elites in random rooms / dead-ends with `elite_*` subtype.
- [ ] One boss in front of the exit portal.
- [ ] Normal / elite / boss drop tables match the vertical-slice loot table.
- [ ] Remote party member levels are not stubbed as local (fix AvgLevel).
- [ ] Deterministic elite personal rolls use the SHA256 seed from the loot plan.

## Out of scope

First-clear cosmetics. Revive downed allies (P2). Mesh unification (ARCH-BE-006).

## Suggested files

- `DungeonInstanceManager.cs`
- `MapGenerator.cs`
- `LootSystem.cs`
- `LootSystemEnhancements.cs`
- `OverworldCombatSync.cs`

## Dependencies

- ARCH-BE-001
- ARCH-BE-002
