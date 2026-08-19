# ARCH-UI-023: Interior enter / exit prompts

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Architecture |
| **Stack** | UI (React / canvas) |
| **Priority** | P2 |
| **Estimate** | S |
| **Status** | Todo |
| **Source** | README.md interiors; Dim Carcosa sealed prompt |

## Summary

E prompt: “Enter …” vs “Sealed.” Exit portal prompt inside interiors.

## Context

ARCH-BE-031 flags `enterable`.

## Acceptance criteria

- [ ] Enterable vs sealed copy from CONT-UI-006.
- [ ] Works for village houses and The Last House.

## Out of scope

Furniture interaction.

## Suggested files

- `OverworldView.tsx`

## Dependencies

- ARCH-BE-031
- CONT-UI-006
