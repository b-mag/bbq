# CONT-BE-012: Dig clue items that mark the map

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | README.md §16 clue items / map fragments |

## Summary

Most digs are empty by design. Clue items / fragments mark productive dig spots on the map (not the three secrets).

## Context

Twelve artifacts sit on deterministic coords. Secrets stay unmarked.

## Acceptance criteria

- [ ] At least a few non-secret artifacts can be hinted via a found fragment.
- [ ] Map markers respect fog except where we explicitly punch through (do not punch secrets).
- [ ] Secrets remain unmarked.

## Out of scope

Shovel grant. All 12 sprites.

## Suggested files

- `QuestProgression.cs`
- `DigSystem.cs`
- `OverworldMapPanel.tsx`

## Dependencies

- ARCH-BE-026
