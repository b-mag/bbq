# ARCH-BE-007: Stable chat messageId and single timestamp

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | Backend (.NET) |
| **Priority** | P0 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0 chat |

## Summary

Every chat message gets one `messageId` and one `UtcNow` stamp so the UI can dedup and `since=` cannot drop a sibling sent in the same millisecond.

## Context

`SendChatAsync` stamps the wire message and the local store with two separate `UtcNow` calls. Harmless until two messages share a millisecond; `Timestamp > since` can drop one.

## Acceptance criteria

- [ ] Each stored message has a unique `messageId` (already on payload — persist it).
- [ ] One timestamp written; wire and local store share it.
- [ ] `GET /api/p2p/chat/messages?since=` is strictly greater-than on (timestamp, messageId) if needed.
- [ ] Nearby `/n` still filters to 15 tiles on receive.

## Out of scope

Frontend poll/focus (ARCH-UI-003). Party membership gate (ARCH-BE-008).

## Suggested files

- `OverworldSync.cs`
- `Program.cs P2P chat endpoints`

## Dependencies

- Pairs with ARCH-UI-003.
