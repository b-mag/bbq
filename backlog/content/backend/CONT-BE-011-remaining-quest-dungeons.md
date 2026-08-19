# CONT-BE-011: Remaining dungeon quest chain

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | README.md See Beyond chain; QuestProgression |

## Summary

After Drowned Dock, See Beyond leads Temple of Hali → Mountain Cave → Sunken Cyclopean Quay → Palace Crypt with distinct clears and shovel on Temple.

## Context

Early chain (Merek / husk / Necronomicon / See Beyond) is implemented. Id collisions currently skip the Quay (ARCH-BE-003).

## Acceptance criteria

- [ ] Each entrance is a distinct defeated id.
- [ ] See Beyond marker moves on each victory; pulse stops when that boss is dead.
- [ ] Player may ignore the marker.
- [ ] Shovel grant stays on Temple (ARCH-BE-026).

## Out of scope

New Necronomicon gameplay pages (ARCH-BE-027). Crypt generator (ARCH-BE-004).

## Suggested files

- `QuestProgression.cs`
- `OverworldWorldGen.cs Entrances`
- `QuestProgressionTests.cs`

## Dependencies

- ARCH-BE-003
- ARCH-BE-026
