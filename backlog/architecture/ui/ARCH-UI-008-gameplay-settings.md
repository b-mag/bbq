# ARCH-UI-008: Settings: player count, names, streamer

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P1 settings |

## Summary

Add the slice settings that already have a pattern from Show FPS: Show connected players, show names/health over players, streamer mode (hide Glyph and IP-ish HUD).

## Context

Settings already: display name, offline mode, master volume stub, Glyph overlay, FPS. Wire new flags through `PlayerSave` + settings POST like `ShowFps`.

## Acceptance criteria

- [ ] Show connected players (default off): HUD `peerCount + 1` or shard `playerCount`.
- [ ] Show names / health over players (default on).
- [ ] Streamer mode (default off): hide Glyph, hide public address / IP-ish HUD.
- [ ] Persisted in save. AOT DTOs if new fields.

## Out of scope

Volume split (ARCH-UI-014). Key rebind (ARCH-UI-020). Screen shake (ARCH-UI-025).

## Suggested files

- `SettingsPanel.tsx`
- `PlayerSave.cs`
- `Program.cs settings`
- `P2POverlay.tsx`

## Dependencies

- None.
