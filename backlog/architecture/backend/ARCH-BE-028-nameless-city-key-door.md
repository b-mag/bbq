# ARCH-BE-028: Nameless City Key door

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | SPRITE_TECHNICAL.md nameless_city_key; README.md §16 |

## Summary

The secret SW-dune key finally opens one door — a nameless interior or sealed ruin that most players will never find.

## Context

Key item exists with no function. Many players will never stand on that dune. Keep it secret; do not add a See Beyond marker for it.

## Acceptance criteria

- [ ] One world object / door checks `nameless_city_key`.
- [ ] Without the key: sealed prompt. With key: enter instance or unlock tiles.
- [ ] Does not appear on See Beyond.

## Out of scope

Large new dungeon. Marketing the secret.

## Suggested files

- `OverworldWorldGen.cs`
- `QuestProgression.cs`
- `DungeonInstanceManager.cs`

## Dependencies

- ARCH-BE-031 if it is an interior. ASSET for the door.
