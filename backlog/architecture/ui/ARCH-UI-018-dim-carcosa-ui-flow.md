# ARCH-UI-018: Dim Carcosa + Last House enter/exit UI

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | implementations/VERTICAL_SLICE_BACKLOG.md Dim Carcosa |

## Summary

When the lake overlay is visible, streets are walkable, The Last House is enterable, sealed buildings prompt. Tooltip: “The cloud-waves recede.” Exit returns to the lake door.

## Context

Do not call it Warehouse or a generic shop zone. Offline: no city, no shop.

## Acceptance criteria

- [ ] Overlay tiles render when `dimCarcosaVisible`.
- [ ] E on The Last House uses the instance enter flow.
- [ ] Sealed structures: “sealed” prompt, no enter.
- [ ] Shop cannot open if matchmaking drops.

## Out of scope

Shop catalog panel (ARCH-UI-019). Street art (ASSET-016).

## Suggested files

- `OverworldView.tsx`
- `OverworldCanvas.tsx`
- `page.tsx`

## Dependencies

- ARCH-BE-014
- ARCH-BE-015
- ARCH-UI-001
