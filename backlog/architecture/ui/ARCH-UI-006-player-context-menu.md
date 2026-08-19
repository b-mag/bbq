# ARCH-UI-006: Player click: invite / whisper / friend

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 party UX |

## Summary

Click (or right-click) another player: Invite / Whisper / Add Friend. Friend key is PeerId.

## Context

Friends panel already persists PeerIds. Click-to-invite exists. Need a small menu instead of always inviting.

## Acceptance criteria

- [ ] Menu: Invite, Whisper (can stub whisper as /w if backend missing — say so), Add/Remove Friend.
- [ ] Uses PeerId, cached display name as label only.
- [ ] Does not open on the local player.

## Out of scope

Full whisper backend if it does not exist — file a follow-up rather than bloating this.

## Suggested files

- `OverworldView.tsx`
- `OverworldCanvas.tsx`
- `FriendsPanel.tsx`

## Dependencies

- Friends persist already shipped.
