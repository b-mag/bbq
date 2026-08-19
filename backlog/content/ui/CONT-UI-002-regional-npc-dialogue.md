# CONT-UI-002: Regional NPC dialogue

| Field | Value |
|-------|-------|
| **Type** | Story |
| **Domain** | Content |
| **Stack** | UI (React) |
| **Priority** | P1 |
| **Estimate** | M |
| **Status** | Todo |
| **Source** | OverworldWorldGen NPC placements; npc-dialogue.ts; Merek chain exists |

## Summary

Give the placed regional NPCs (marsh, hermit, ashwalker, ferryman, widow, ranger, priest, ember, Cassilda) short Chambers/Lynch lines. Not quest-critical except where already wired.

## Context

Many `npc_*` objects are placed with no or stub dialogue. Merek is the early quest. Do not turn everyone into a quest hub.

## Acceptance criteria

- [ ] Each placed named NPC has ≥2 lines.
- [ ] Tone matches region (swamp rot, ash cough, lake ferry, mountain cold).
- [ ] Talk still goes through `POST /api/gameplay/npc-talk` if that is the path; otherwise local dialogue ids — pick one and document.

## Out of scope

The Stranger (CONT-BE-010). Full quests per NPC.

## Suggested files

- `src/frontend/lib/npc-dialogue.ts`
- `QuestProgression.cs if server-authored`

## Dependencies

- None.
