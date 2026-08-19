# ARCH-BE-026: Grant Obsidian Shovel on Temple of Hali clear

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | README.md §16; DigSystem.cs header |

## Summary

Temple of Hali boss victory grants Key Item `obsidian_shovel`. Until then G-to-dig keeps explaining the missing tool.

## Context

Digging exists (`POST /api/gameplay/dig`) but returns “no shovel”. Suggested grant is Temple of Hali, not the early Merek quest.

## Acceptance criteria

- [ ] `NotifyDungeonComplete` for `temple_of_hali` (or current temple id until ARCH-BE-003) grants the shovel once.
- [ ] Key Items panel lists it. Duplicate clears do not duplicate the item.
- [ ] G on a diggable tile succeeds after grant.

## Out of scope

Clue map fragments (CONT-BE-012). Artifact sprites.

## Suggested files

- `QuestProgression.cs`
- `ItemRegistry.cs`
- `src/tests/QuestProgressionTests.cs`

## Dependencies

- ARCH-BE-001 so the temple can actually be cleared.
