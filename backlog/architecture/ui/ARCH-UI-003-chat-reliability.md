# ARCH-UI-003: Chat reliability: dedup, poll, ESC, X

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P0 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0 chat |

## Summary

Chat must survive heavy use: dedup on messageId, one in-flight poll, React keys, focus/ESC/X, cap history at 50.

## Context

500ms poll overlaps, index keys, focus desync gates movement, ESC in OverworldView returns immediately while chatFocused, no X, Party + Glyph overlap top-right.

## Acceptance criteria

- [ ] Dedup on `messageId`; `key={messageId}`.
- [ ] Single in-flight poll (abort or ignore stale).
- [ ] Enter focuses only if the input is not already `document.activeElement`; desynced focused-but-blurred resets.
- [ ] ESC closes chat first via `ui-stack`, then other panels.
- [ ] Visible X / “press ESC to close chat”.
- [ ] Rendered history capped at 50.
- [ ] Chat registers on `ui-stack`.

## Out of scope

Party membership of /p (ARCH-BE-008). Whisper.

## Suggested files

- `OverworldChat.tsx`
- `OverworldView.tsx`
- `ui-stack.ts`

## Dependencies

- ARCH-BE-007
