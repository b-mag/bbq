# ARCH-UI-017: Shard dropdown when tracker is online

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 matchmaking shard dropdown |

## Summary

If tracker is up and `worlds.length > 1`, Settings or P2P overlay shows a dropdown `23/100`. Selecting one calls existing `SwitchShardAsync`. Hide if only one world. Offline: no dropdown.

## Context

`useP2POverworld.switchShard` already POSTs `/api/p2p/shard/switch`.

## Acceptance criteria

- [ ] Dropdown only when tracker online and more than one world.
- [ ] Population shown per option.
- [ ] Switch uses existing API then re-registers.

## Out of scope

Friends bias logic (ARCH-BE-018).

## Suggested files

- `P2POverlay.tsx`
- `SettingsPanel.tsx`
- `useP2POverworld.ts`

## Dependencies

- ARCH-BE-013
