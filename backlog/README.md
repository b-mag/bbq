# Carcosa backlog

Jira-sized units of work. One story per file. Pulled from `implementations/VERTICAL_SLICE_BACKLOG.md`, `README.md` §17, `implementations/OVERWORLD_VISION.md`, `docs/SPRITE_TECHNICAL.md`, `implementations/P2P_LOOT_DISTRIBUTION_PLAN.md`, `implementations/NAT_TURN_GAP.md`, plus new audio / weather / landmark work that is not in those docs yet.

**Created:** 2026-08-16  
**Does not replace** `implementations/VERTICAL_SLICE_BACKLOG.md` — that file stays the vertical-slice diagnosis. These stories are the work tickets.

---

## How to read a story

| Field | Meaning |
|-------|---------|
| **Domain** | `Architecture` (systems, plumbing, UX chrome) or `Content` (world, quest, flavor, art) |
| **Stack** | `UI` (React/Next/canvas), `Backend` (.NET / AOT), or `Assets` (PNG, tiles, SFX, music) |
| **Priority** | `P0` slice-blocker · `P1` needed for a real game · `P2` polish · `PARKED` do not start |
| **Estimate** | `S` ≤1 day · `M` 2–3 days · `L` ~1 week |
| **Status** | `Todo` until someone is working it |

Each file is independently completable. If a ticket feels like an epic, it was split too large — split it again.

---

## Folder layout

```
backlog/
├── README.md                          ← this index
├── architecture/
│   ├── backend/                       ← .NET systems
│   └── ui/                            ← React / canvas / HUD chrome
└── content/
    ├── backend/                       ← world gen, quests, tables
    ├── ui/                            ← copy, dialogue, HUD flavor
    └── assets/                        ← tiles, sprites, SFX, music
```

---

## ID scheme

| Prefix | Domain | Stack |
|--------|--------|-------|
| `ARCH-BE-nnn` | Architecture | Backend .NET |
| `ARCH-UI-nnn` | Architecture | UI |
| `CONT-BE-nnn` | Content | Backend .NET |
| `CONT-UI-nnn` | Content | UI |
| `ASSET-nnn` | Content | Assets (art / audio) |

---

## Suggested next sprint (vertical slice first)

1. `ARCH-BE-001` + `ARCH-UI-001` — dungeon actually loads  
2. `ARCH-BE-002` + `ARCH-UI-002` — exit + resume at the door  
3. `ARCH-UI-003` — chat stops dying  
4. `ARCH-UI-004` — ESC / close buttons  
5. `ARCH-UI-012` — sound player (unblocks all audio assets)  
6. `ARCH-BE-022` — world clock + weather sim (unblocks weather art/SFX)

Audio and weather are **P1**, not parked. They are not required to unstick dungeons, but they are the next atmosphere systems after the slice is playable.

NAT/TURN (`ARCH-BE-020`, `ARCH-BE-021`) stay **PARKED**.

---

## Architecture — Backend (.NET)

| ID | Priority | Title |
|----|----------|-------|
| [ARCH-BE-001](architecture/backend/ARCH-BE-001-dungeon-instance-load.md) | P0 | Solo dungeon loads from DungeonInstanceManager |
| [ARCH-BE-002](architecture/backend/ARCH-BE-002-dungeon-exit-portal.md) | P0 | Dungeon exit portal returns to the entrance |
| [ARCH-BE-003](architecture/backend/ARCH-BE-003-split-dungeon-scenario-ids.md) | P0 | Split drowned_dock / warehouse / sunken_quay ids |
| [ARCH-BE-004](architecture/backend/ARCH-BE-004-palace-crypt-generator.md) | P1 | Distinct Palace Crypt map generator |
| [ARCH-BE-005](architecture/backend/ARCH-BE-005-dungeon-elites-boss-loot.md) | P0 | Instanced dungeon trash, elites, boss, loot |
| [ARCH-BE-006](architecture/backend/ARCH-BE-006-unify-dungeon-onto-mesh.md) | P1 | Unify dungeon onto the mesh (retire /ws GameLoop) |
| [ARCH-BE-007](architecture/backend/ARCH-BE-007-chat-message-ids.md) | P0 | Stable chat messageId and single timestamp |
| [ARCH-BE-008](architecture/backend/ARCH-BE-008-party-chat-membership.md) | P1 | Gate /p chat to party membership |
| [ARCH-BE-009](architecture/backend/ARCH-BE-009-flame-cryptol-drip.md) | P1 | Flame offer 1% Cryptol drip + response DTO |
| [ARCH-BE-010](architecture/backend/ARCH-BE-010-combat-heal-events.md) | P1 | Combat heal events for frontend VFX |
| [ARCH-BE-011](architecture/backend/ARCH-BE-011-loot-eligibility-windows.md) | P1 | Loot eligibility windows and visibility |
| [ARCH-BE-012](architecture/backend/ARCH-BE-012-autonomous-loot-pickup.md) | P1 | Autonomous loot pickup broadcast |
| [ARCH-BE-013](architecture/backend/ARCH-BE-013-tracker-worlds-endpoint.md) | P1 | Tracker GET /worlds population |
| [ARCH-BE-014](architecture/backend/ARCH-BE-014-dim-carcosa-tracker-gate.md) | P1 | Dim Carcosa overlay gated on tracker |
| [ARCH-BE-015](architecture/backend/ARCH-BE-015-last-house-fixed-instance.md) | P1 | Last House fixed instance |
| [ARCH-BE-016](architecture/backend/ARCH-BE-016-matchmaking-shop-api.md) | P1 | Matchmaking shop catalog + buy |
| [ARCH-BE-017](architecture/backend/ARCH-BE-017-dev-starting-cryptol.md) | P1 | DevStartingCryptol flag (0 in release) |
| [ARCH-BE-018](architecture/backend/ARCH-BE-018-friends-biased-shards.md) | P1 | Friends-biased shard assignment |
| [ARCH-BE-019](architecture/backend/ARCH-BE-019-mesh-split-neighborhood.md) | P2 | Mesh-split bounded neighborhood |
| [ARCH-BE-020](architecture/backend/ARCH-BE-020-turn-relay.md) | PARKED | TURN relay fallback |
| [ARCH-BE-021](architecture/backend/ARCH-BE-021-ipv6-glyphs.md) | PARKED | IPv6 Glyph codec |
| [ARCH-BE-022](architecture/backend/ARCH-BE-022-world-clock-weather-sim.md) | P1 | World clock and weather simulation |
| [ARCH-BE-023](architecture/backend/ARCH-BE-023-weather-mesh-broadcast.md) | P1 | Weather state mesh broadcast |
| [ARCH-BE-024](architecture/backend/ARCH-BE-024-biome-movement-modifiers.md) | P1 | Biome movement modifiers |
| [ARCH-BE-025](architecture/backend/ARCH-BE-025-wire-dig-artifact-passives.md) | P1 | Wire dig artifact passives |
| [ARCH-BE-026](architecture/backend/ARCH-BE-026-grant-obsidian-shovel.md) | P1 | Grant Obsidian Shovel on Temple of Hali |
| [ARCH-BE-027](architecture/backend/ARCH-BE-027-necronomicon-later-functions.md) | P1 | Necronomicon functions after See Beyond |
| [ARCH-BE-028](architecture/backend/ARCH-BE-028-nameless-city-key-door.md) | P2 | Nameless City Key door |
| [ARCH-BE-029](architecture/backend/ARCH-BE-029-second-sun-lens.md) | P2 | Second-Sun Lens day/night swap |
| [ARCH-BE-030](architecture/backend/ARCH-BE-030-overworld-death-respawn.md) | P2 | Overworld death and respawn |
| [ARCH-BE-031](architecture/backend/ARCH-BE-031-building-interior-instances.md) | P2 | Enterable building interiors as instances |

## Architecture — UI

| ID | Priority | Title |
|----|----------|-------|
| [ARCH-UI-001](architecture/ui/ARCH-UI-001-dungeon-rest-snapshot.md) | P0 | Render dungeon from REST snapshot, not /ws lobby |
| [ARCH-UI-002](architecture/ui/ARCH-UI-002-resume-at-dungeon-door.md) | P0 | Resume at dungeon door from save |
| [ARCH-UI-003](architecture/ui/ARCH-UI-003-chat-reliability.md) | P0 | Chat reliability: dedup, poll, ESC, X |
| [ARCH-UI-004](architecture/ui/ARCH-UI-004-esc-ui-stack.md) | P1 | ESC ui-stack for Glyph, chat, party, inspect |
| [ARCH-UI-005](architecture/ui/ARCH-UI-005-party-panel.md) | P1 | Party panel leave / kick |
| [ARCH-UI-006](architecture/ui/ARCH-UI-006-player-context-menu.md) | P1 | Player click: invite / whisper / friend |
| [ARCH-UI-007](architecture/ui/ARCH-UI-007-inspect-overlay.md) | P1 | Inspect overlay |
| [ARCH-UI-008](architecture/ui/ARCH-UI-008-gameplay-settings.md) | P1 | Settings: player count, names, streamer |
| [ARCH-UI-009](architecture/ui/ARCH-UI-009-overworld-combat-vfx.md) | P1 | Overworld combat VFX |
| [ARCH-UI-010](architecture/ui/ARCH-UI-010-aoe-ghost-preview.md) | P1 | AoE ghost preview |
| [ARCH-UI-011](architecture/ui/ARCH-UI-011-floating-combat-numbers.md) | P1 | Damage / heal floating numbers |
| [ARCH-UI-012](architecture/ui/ARCH-UI-012-web-audio-player.md) | P1 | Web Audio sound player (buses) |
| [ARCH-UI-013](architecture/ui/ARCH-UI-013-weather-canvas-overlay.md) | P1 | Weather canvas overlay |
| [ARCH-UI-014](architecture/ui/ARCH-UI-014-volume-sliders-wired.md) | P1 | Wire volume sliders to the mixer |
| [ARCH-UI-015](architecture/ui/ARCH-UI-015-loot-pickup-prompt.md) | P2 | Loot pickup prompt |
| [ARCH-UI-016](architecture/ui/ARCH-UI-016-overworld-party-hp.md) | P2 | Overworld party HP bars |
| [ARCH-UI-017](architecture/ui/ARCH-UI-017-shard-dropdown.md) | P1 | Shard dropdown when tracker is online |
| [ARCH-UI-018](architecture/ui/ARCH-UI-018-dim-carcosa-ui-flow.md) | P1 | Dim Carcosa + Last House enter/exit UI |
| [ARCH-UI-019](architecture/ui/ARCH-UI-019-cryptol-shop-panel.md) | P1 | Live Cryptol shop panel |
| [ARCH-UI-020](architecture/ui/ARCH-UI-020-key-rebind.md) | P1 | Key rebind table |
| [ARCH-UI-021](architecture/ui/ARCH-UI-021-weather-hud.md) | P2 | Weather / time-of-day HUD |
| [ARCH-UI-022](architecture/ui/ARCH-UI-022-death-respawn-overlay.md) | P2 | Death / respawn overlay |
| [ARCH-UI-023](architecture/ui/ARCH-UI-023-interior-prompts.md) | P2 | Interior enter / exit prompts |
| [ARCH-UI-024](architecture/ui/ARCH-UI-024-dungeon-loading-flavor.md) | P2 | Dungeon loading flavor |
| [ARCH-UI-025](architecture/ui/ARCH-UI-025-screen-shake-aim-preview.md) | P1 | Screen shake + ability aim setting |

## Content — Backend (.NET)

| ID | Priority | Title |
|----|----------|-------|
| [CONT-BE-001](content/backend/CONT-BE-001-drowned-dock-rename-leftovers.md) | P0 | Remaining Warehouse → Drowned Dock labels |
| [CONT-BE-002](content/backend/CONT-BE-002-place-pyramids.md) | P1 | Place large pyramids on the overworld |
| [CONT-BE-003](content/backend/CONT-BE-003-scatter-obelisks.md) | P1 | Scatter obelisks across the land |
| [CONT-BE-004](content/backend/CONT-BE-004-place-megaliths.md) | P1 | Place stonehenge-like megalith circles |
| [CONT-BE-005](content/backend/CONT-BE-005-great-swamp-density.md) | P1 | Great Swamp density and encounters |
| [CONT-BE-006](content/backend/CONT-BE-006-snow-ice-biome.md) | P1 | Northern snow / ice biome content |
| [CONT-BE-007](content/backend/CONT-BE-007-regional-weather-tables.md) | P1 | Regional weather weight tables |
| [CONT-BE-008](content/backend/CONT-BE-008-dreamlike-weather-events.md) | P1 | Dreamlike / Lynch / Giger weather events |
| [CONT-BE-009](content/backend/CONT-BE-009-dim-carcosa-streets.md) | P1 | Dim Carcosa drowned street layout |
| [CONT-BE-010](content/backend/CONT-BE-010-stranger-shop-sku.md) | P1 | The Stranger NPC and shop SKU |
| [CONT-BE-011](content/backend/CONT-BE-011-remaining-quest-dungeons.md) | P1 | Remaining dungeon quest chain |
| [CONT-BE-012](content/backend/CONT-BE-012-dig-clue-map-markers.md) | P2 | Dig clue items that mark the map |
| [CONT-BE-013](content/backend/CONT-BE-013-biome-enemy-tables.md) | P1 | Biome-specific enemy spawn tables |
| [CONT-BE-014](content/backend/CONT-BE-014-west-hamlet-fill.md) | P2 | West Hamlet fill |
| [CONT-BE-015](content/backend/CONT-BE-015-dark-forest-encounters.md) | P2 | Dark Forest paths and encounters |
| [CONT-BE-016](content/backend/CONT-BE-016-landmark-registry.md) | P1 | Landmark entries for new world objects |

## Content — UI

| ID | Priority | Title |
|----|----------|-------|
| [CONT-UI-001](content/ui/CONT-UI-001-drowned-dock-copy.md) | P0 | Drowned Dock HUD / copy leftovers |
| [CONT-UI-002](content/ui/CONT-UI-002-regional-npc-dialogue.md) | P1 | Regional NPC dialogue |
| [CONT-UI-003](content/ui/CONT-UI-003-weather-flavor-toasts.md) | P1 | Weather flavor toasts |
| [CONT-UI-004](content/ui/CONT-UI-004-dungeon-enter-strings.md) | P2 | Dungeon enter flavor strings |
| [CONT-UI-005](content/ui/CONT-UI-005-map-landmark-labels.md) | P1 | Map labels for pyramids / obelisks / megaliths |
| [CONT-UI-006](content/ui/CONT-UI-006-sealed-building-copy.md) | P2 | Sealed vs enterable building copy |
| [CONT-UI-007](content/ui/CONT-UI-007-artifact-keyitem-flavor.md) | P1 | Dig artifact Key Items flavor |
| [CONT-UI-008](content/ui/CONT-UI-008-see-beyond-area-names.md) | P1 | See Beyond area names |

## Content — Assets

| ID | Priority | Title |
|----|----------|-------|
| [ASSET-001](content/assets/ASSET-001-pyramid-sprites.md) | P1 | Large pyramid sprites |
| [ASSET-002](content/assets/ASSET-002-obelisk-sprites.md) | P1 | Obelisk sprite variants |
| [ASSET-003](content/assets/ASSET-003-megalith-sprites.md) | P1 | Megalith / stonehenge sprites |
| [ASSET-004](content/assets/ASSET-004-snow-ice-tiles.md) | P1 | Snow / ice tiles and black-star sky |
| [ASSET-005](content/assets/ASSET-005-great-swamp-tiles.md) | P1 | Great swamp tiles |
| [ASSET-006](content/assets/ASSET-006-weather-particle-sheets.md) | P1 | Rain / fog / snow particle sheets |
| [ASSET-007](content/assets/ASSET-007-lynch-giger-overlays.md) | P1 | Lynch / Giger weather overlays |
| [ASSET-008](content/assets/ASSET-008-footstep-sfx.md) | P1 | Footstep SFX per biome |
| [ASSET-009](content/assets/ASSET-009-combat-sfx.md) | P1 | Combat SFX |
| [ASSET-010](content/assets/ASSET-010-world-sfx.md) | P1 | World interaction SFX |
| [ASSET-011](content/assets/ASSET-011-ambient-music-beds.md) | P1 | Ambient music beds per biome |
| [ASSET-012](content/assets/ASSET-012-weather-audio-loops.md) | P1 | Weather audio loops |
| [ASSET-013](content/assets/ASSET-013-dream-weather-stingers.md) | P1 | Dream weather stingers |
| [ASSET-014](content/assets/ASSET-014-old-book-husk-final.md) | P1 | old_book_husk final art |
| [ASSET-015](content/assets/ASSET-015-dig-artifact-sprites.md) | P2 | Dig artifact trinket sprites |
| [ASSET-016](content/assets/ASSET-016-dim-carcosa-tiles.md) | P1 | Dim Carcosa drowned city tiles |
| [ASSET-017](content/assets/ASSET-017-last-house-stranger.md) | P1 | Last House interior + Stranger sprite |
| [ASSET-018](content/assets/ASSET-018-interior-house-tiles.md) | P2 | Interior house tiles |
| [ASSET-019](content/assets/ASSET-019-palace-crypt-tiles.md) | P1 | Palace Crypt distinct tiles |
| [ASSET-020](content/assets/ASSET-020-snow-props.md) | P2 | Snow region props |

---

## Invariants (do not violate in any story)

1. No central game server. Matchmaking is discovery / shop only.
2. Quest, Key Items, Friends, dig spots, fog-of-war stay **local**. Never broadcast on `/ws/peer`.
3. Register every new HTTP/P2P/save DTO on the matching `JsonContext` (AOT).
4. Friends ≠ Party. Party = combat/loot. Friends = future neighborhood preference.
5. Weather and audio are **cosmetic + local feel** except host-broadcast weather *state* (so peers see the same rain). Player fog-of-war is still private.
