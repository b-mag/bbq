# ARCH-UI-004: ESC ui-stack for Glyph, chat, party, inspect

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 windows |

## Summary

Every overlay registers on `ui-stack`. ESC always pops the top. Every overlay has an X. Glyph is a first-class panel.

## Context

`ui-stack.ts` describes LIFO ESC. `OverworldView` uses a hardcoded if/else and never lists Glyph or chat. Pause toggle is buggy if chat is focused.

## Acceptance criteria

- [ ] Pause, Settings, Inventory, Flame, Ability select, Glyph, Chat, Party invite toast, Inspect, Friends, Map all on the stack.
- [ ] ESC pops top only; does not skip to pause.
- [ ] Each overlay has a close control.
- [ ] Glyph no longer only “Hide Glyph” without ESC.

## Out of scope

Key rebind (ARCH-UI-020).

## Suggested files

- `ui-stack.ts`
- `OverworldView.tsx`
- `P2POverlay.tsx`
- `PauseMenu.tsx`

## Dependencies

- ARCH-UI-003 for chat.
