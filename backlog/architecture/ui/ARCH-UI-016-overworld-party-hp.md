# ARCH-UI-016: Overworld party HP bars

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P2 |

## Summary

Dungeon HUD has party HP; overworld party list is names only. Show compact HP for party members on the overworld.

## Context

Needs meshed HP on player state (already on combat snapshot for local; confirm remotes).

## Acceptance criteria

- [ ] Party members show HP bars on overworld HUD.
- [ ] Non-party peers do not, unless names/health setting is on (nameplates).

## Out of scope

Raid frames for 100-player shards.

## Suggested files

- `OverworldView.tsx`
- `HealthBar.tsx`
- `P2POverlay.tsx`

## Dependencies

- ARCH-UI-005 helps but is not required.
