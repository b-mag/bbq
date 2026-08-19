# ARCH-BE-012: Autonomous loot pickup broadcast

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | P2P_LOOT_DISTRIBUTION_PLAN.md Pickup Authority |

## Summary

A non-host peer picks up locally, broadcasts removal, all peers remove. Eventual consistency with timestamp anti-replay.

## Context

Pickup must not ask the shard host for permission. Host still runs enemy AI; loot pickup is peer-authoritative for eligible drops.

## Acceptance criteria

- [ ] Eligible peer can pickup without host RPC approval.
- [ ] Removal event includes drop id + timestamp; late/replay ignored.
- [ ] Collected drops hidden for everyone.
- [ ] Solo (no mesh) still pickups locally.

## Out of scope

Frontend E-prompt (ARCH-UI-015).

## Suggested files

- `LootSystem.cs`
- `OverworldSync.cs`
- `PeerMessagePayloads.cs`
- `PeerJsonContext.cs`

## Dependencies

- ARCH-BE-011
