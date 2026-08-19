# ARCH-UI-005: Party panel leave / kick

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 party |

## Summary

Party panel shows member list, Leave, and leader Kick. ESC closes it. Invite failures toast.

## Context

APIs exist: leave, leader invite-only, max 8, leader failover. No Leave button. Clicking a name only invites. No kick/promote/disband UI.

## Acceptance criteria

- [ ] Leave calls `POST /api/p2p/party/leave`.
- [ ] Leader Kick (and optionally Promote) with existing APIs if present; otherwise add the missing POST in the same PR.
- [ ] When a member enters a dungeon, others get “X entered The Drowned Dock — Join”.
- [ ] Invite-while-already-in-party shows the error instead of silence.

## Out of scope

Inspect (ARCH-UI-007). Whisper (ARCH-UI-006).

## Suggested files

- `OverworldView.tsx`
- `new PartyPanel.tsx`
- `Program.cs /api/p2p/party/*`

## Dependencies

- ARCH-UI-004
