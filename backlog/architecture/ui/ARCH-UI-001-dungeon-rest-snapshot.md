# ARCH-UI-001: Render dungeon from REST snapshot, not /ws lobby

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P0 |
| **Estimate** | L |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md P0-D1 |

## Summary

After `dungeon/enter` succeeds, the React app renders the instance from the REST map snapshot. Stop opening `/ws` lobby / “Entering Dungeon…” for mesh overworld entry.

## Context

`page.tsx` switches to `appState === 'dungeon'` and opens the old wave WebSocket. `OverworldView.enterDungeon` historically hardcoded `mountain_cave`. Player is already invisible on the overworld (`in_dungeon`).

## Acceptance criteria

- [ ] E at an entrance loads that entrance’s scenario (pass `entrance.scenario`).
- [ ] No `/ws` lobby for this path; canvas shows tiles/entities from the snapshot + combat poll.
- [ ] Solo works with nobody else online.
- [ ] “Entering Dungeon…” cannot hang forever — timeout + error toast if snapshot fails.

## Out of scope

Mesh unification of combat (ARCH-BE-006) can land after a REST snapshot works.

## Suggested files

- `src/frontend/app/page.tsx`
- `OverworldView.tsx`
- `GameCanvas.tsx`
- `hooks for dungeon snapshot`

## Dependencies

- ARCH-BE-001
