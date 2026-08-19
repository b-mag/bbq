# ARCH-BE-031: Enterable building interiors as instances

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | README.md §16 interiors; VERTICAL_SLICE Dim Carcosa sealed buildings |

## Summary

Some houses become real interior maps (fixed, small) using the same enter/exit instance rules as The Last House. Most buildings stay sealed.

## Context

Village houses are collision props today. Dim Carcosa wants lots of sealed shells and one enterable Last House. A generic interior instance type avoids a new protocol.

## Acceptance criteria

- [ ] World objects can flag `enterable` + `interiorId`.
- [ ] Enter vanishes from overworld; exit returns to the door tile.
- [ ] At least one village house and The Last House share the mechanism.
- [ ] Sealed buildings do not enter.

## Out of scope

Art for every house (ASSET-018). All NPCs moving inside.

## Suggested files

- `OverworldWorldGen.cs`
- `DungeonInstanceManager.cs`
- `Program.cs`

## Dependencies

- ARCH-BE-015 can ship Last House first as the prototype.
