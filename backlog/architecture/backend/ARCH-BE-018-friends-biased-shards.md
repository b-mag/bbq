# ARCH-BE-018: Friends-biased shard assignment

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 friends; README Friends ≠ Party |

## Summary

When matchmaking would place you in world N+1 but a Friend is in world N with room, prefer N. Friends never punch a hole in the 100 cap. Glyph to a friend still joins their shard.

## Context

Friends list is persisted (`SavedFriend` / PeerId). Mesh-split algorithm does not exist yet (ARCH-BE-019). This ticket is assignment/reconnect bias only. Reconnect should try friends’ last addresses before generic `known-peers.json` order.

## Acceptance criteria

- [ ] Tracker/assignment prefers a friend’s shard if that shard has room.
- [ ] Full shard (100) still rejects; no cap bypass.
- [ ] Glyph connect to a friend joins their world (already true — keep it).
- [ ] Cache bootstrap tries friend addresses first.
- [ ] Identity is PeerId, never display name.

## Out of scope

Bounded neighborhood split (ARCH-BE-019).

## Suggested files

- `TrackerClient.cs`
- `PeerExchange.cs`
- `QuestProgression.cs`
- `src/matchmaking/Program.cs`

## Dependencies

- Friends persist already exists.
