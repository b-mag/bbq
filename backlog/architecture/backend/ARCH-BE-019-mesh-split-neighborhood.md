# ARCH-BE-019: Mesh-split bounded neighborhood

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P2 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | README.md §16; NAT_TURN_GAP.md bounded neighborhood; mesh-shard-network-plan.md |

## Summary

When a cluster is too large, split shards / bound combat sync to nearby peers, preferring to keep Friends together. Do not use Friends for loot rights.

## Context

Full mesh is O(n²) at 100 peers. Party already covers combat/loot grouping. This algorithm does not exist yet.

## Acceptance criteria

- [ ] Documented split/neighborhood algorithm with Friends as a soft constraint.
- [ ] Combat/state sync radius or neighborhood cap is enforced.
- [ ] Friends in range stay in the same neighborhood when possible.
- [ ] Solo and party dungeons still never require matchmaking.

## Out of scope

TURN (ARCH-BE-020). Loot rights changes.

## Suggested files

- `WorldShard.cs`
- `PeerMesh.cs`
- `OverworldSync.cs`
- `QuestProgression.cs`

## Dependencies

- ARCH-BE-018
