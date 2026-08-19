# ARCH-BE-008: Gate /p chat to party membership

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 party |

## Summary

`/p` is membership-gated, not a color tag. Non-members never receive party chat.

## Context

`MeshPartyManager` already has membership. Chat currently paints `/p` without checking the party list.

## Acceptance criteria

- [ ] Send `/p` only to current party peer ids (plus self echo).
- [ ] Players who leave the party stop receiving `/p`.
- [ ] Solo player using `/p` gets a short error, not a silent global send.

## Out of scope

Whisper UI (ARCH-UI-006).

## Suggested files

- `MeshPartyManager.cs`
- `OverworldSync.cs`
- `Program.cs`

## Dependencies

- ARCH-BE-007
