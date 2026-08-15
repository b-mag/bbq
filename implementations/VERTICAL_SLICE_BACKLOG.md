# Carcosa Vertical Slice — Implementation Backlog

**Created:** 2026-08-15  
**Updated:** 2026-08-15 (flame-anywhere, Cryptol 1% drip, Dim Carcosa shop when matchmaking is up)  
**Context:** Cross-state Glyph join succeeded (1 remote player). This backlog is the fun, playable slice — not NAT/Glyph plumbing.  
**Source of truth for current behavior:** `src/backend/**` and `src/frontend/**` as of this date. Technical docs are stale in places (they still describe a WebSocket-only mesh).

---

## How to read this

| Tag | Meaning |
|-----|---------|
| **P0** | Broken or missing; slice is not playable without it |
| **P1** | Needed for the slice to feel like a game instead of a tech demo |
| **P2** | Polish that makes it *fun* once P0/P1 work |
| **PARKED** | Real work, deliberately deferred (Glyph/NAT after the successful test) |

Each item lists **current code**, **desired behavior**, and **where to start**.

Suggested implementation order is at the bottom.

---

## UPnP / NAT-PMP — does it need a server?

**No.** It does not need the matchmaking server, Kafka, or any Carcosa-hosted process.

`UpnpPortMapper` already talks **from the player's machine to their home router** (SSDP discovery + SOAP `AddPortMapping` on the IGD). Same pattern as BitTorrent. The only “server” involved is the router in the player’s house.

- If the router has UPnP enabled: the game can open its own TCP listen port.
- If not: it fails quietly (already the case).
- It does **not** help two players on symmetric NAT / CGNAT. That would be TURN, which *would* be a hosted relay — also parked.

**Decision:** leave UPnP/NAT-PMP and TURN in PARKED. Do not block the vertical slice on them.

---

## P0 — Instanced dungeons (the live bug)

### What testers saw

Pressing **E** at The Warehouse made the player **invisible on the overworld** and **never loaded a dungeon**.

### Why (root cause)

Three separate systems are glued together incorrectly:

1. `POST /api/gameplay/dungeon/enter` → `DungeonInstanceManager.EnterDungeonAsync` **does** create an in-process instance (seed, map, host election) and calls `OverworldCombatSync.MarkEnteredDungeon`.
2. `MarkEnteredDungeon` sets status to `in_dungeon`. `OverworldCanvas` **skips drawing** anyone with that status. Remote testers see you vanish. Locally you also leave the overworld view.
3. The React app then switches to `appState === 'dungeon'` and opens the **old** dungeon WebSocket (`/ws` → `GameLoop` / lobby / wave shooter). That loop only auto-starts a dungeon if the exe was launched with `--seed=` and `--scenario=`. A normal player exe is **not** in that mode, so **no map is ever sent**. The UI sits on “Entering Dungeon…” / “Loading dungeon…”.

Additional bugs in the same path:

- Frontend **hardcodes** `scenario: 'mountain_cave'` even when the entrance is the warehouse (`OverworldView.enterDungeon`).
- There is **no exit portal**. `CompleteDungeonAsync` exists on the backend; nothing in the dungeon UI calls it except old wave `game_over` / `victory` events that never fire.
- Save already stores `WasInDungeon` + `LastSafeOverworldX/Y` (so a quit *should* resume in front of the door). The overworld frontend **ignores this** and always teleports to the village `spawnPoint` on map load.

### P0-D1 — Load into the instance (first step, as requested)

**Desired:** E at an entrance loads *that* dungeon on this same exe. Solo works with nobody else online.

Implementation sketch:

- Stop using `page.tsx` dungeon `/ws` lobby for mesh overworld entry.
- After `dungeon/enter` succeeds, render the instance from `DungeonInstanceManager.ActiveMap` (new REST snapshot of tiles + entities), driven by the existing overworld combat tick (`OverworldCombatSync`) **or** by feeding the generated `TileMap` into `GameLoop` in-process without a second process.
- Pass `entrance.scenario` through instead of hardcoding `mountain_cave`.
- Keep the player’s overworld status `in_dungeon` (correct: they should vanish from the overworld).
- Party members: keep the existing `GET /api/gameplay/dungeon` poll so non-leaders get pulled in once the leader’s instance is active. Fix this *after* solo load works.

### P0-D2 — Exit portal back to the overworld

**Desired:** End of dungeon has a teleport tile/zone. Stepping on it (or interacting) returns the player to the overworld **just outside the entrance they used**.

- Call `POST /api/gameplay/dungeon/complete`.
- `MarkLeftDungeon` already restores `LastSafeOverworldX/Y` on the backend.
- Frontend must apply that position on return (today it does not).

### P0-D3 — Quit / crash inside a dungeon

**Desired:** Next launch resumes **in front of that dungeon entrance**, not in the village.

- Backend save fields already exist (`PlayerSave.WasInDungeon`, `LastSafeOverworldX/Y`). Combat sync already restores them onto `_localPlayer`.
- **Fix the frontend:** `OverworldView` map-load must spawn from save / player-stats position when `WasInDungeon` (or always from `LastX/LastY`), not from `spawnPoint`.
- Clear `WasInDungeon` after successful overworld resume so a later village logout does not keep snapping to the door.

### P0-D4 — Rename “The Warehouse”

The scenario enum is already `DrownedDock` (`warehouse` is a legacy alias). The overworld label is still `"The Warehouse"`.

**Rename to:** **The Drowned Dock**  
(King in Yellow / Hali coastal gate. Matches `MapScenario.DrownedDock` and the vision doc’s “sunken cyclopean quay”.)

Also rename in: `OverworldBootstrap` entrance name, frontend copy, matchmaking analytics labels if they still say Warehouse.

Keep wire key `drowned_dock` (accept `warehouse` as deprecated alias — already does).

### P0-D5 — Dungeon contents (after load + exit work)

Procedural instance rules (solo or party):

| Rule | Detail |
|------|--------|
| Seed | Rolled at enter; all party members generate the **same** map from that seed |
| Level | Enemies scale to **average party level** (`AvgLevel` is already on the instance snapshot; remote levels currently stubbed as local — fix that) |
| Layout | Existing generators: Drowned Dock 80×60, Temple 100×100, Mountain Cave 60×50 |
| Trash | Packed along the route, level-appropriate HP/damage |
| Elites | Random rooms / dead-ends; `elite_*` subtype so loot uses the elite path |
| Boss | One at the end, in front of the exit portal |
| Party | Same instance, one host (already elected). Members share the map. Downed allies can be revived later (P2). |

Do **not** spawn a second Carcosa.Server process per dungeon.

---

## Loot — elites, bosses, discards (design to implement)

There is a plan file (`P2P_LOOT_DISTRIBUTION_PLAN.md`) whose Phase 1 boxes are still unchecked. Overworld elite drops exist in `OverworldCombatSync`; dungeon loot is unused because dungeons never load.

### Drop table (vertical slice)

| Source | Who can loot | What drops | Notes |
|--------|----------------|------------|--------|
| Normal enemy | Killer, or any party member (any-one) | 0–1 Common / Uncommon | 60s personal → then fair → despawn 120s (plan) |
| Elite (`elite_*`) | Each attacker rolls a **personal** drop from a deterministic seed | 1 Uncommon or Rare (small Epic chance) | Seed: `SHA256(eliteId + peerId + tick + worldId)` — already specified in the loot plan |
| Boss | Each party member | 1 guaranteed Rare+, chance of Epic | Boss is not “fair game” — everyone who entered the instance is eligible |
| First clear (optional P2) | Party | Cosmetic / Pale Marks bonus | Skip for slice if time is tight |

### Discard / Offer to the Flame (economy)

**Decision (2026-08-15):** Offer to the Flame is a **global action**. Press **F** (or Inventory → Offer) from anywhere on the overworld. **No physical Meditation Altar on the map.** If altars return later, they will be tied to a different system, not item discard.

`POST /api/gameplay/offer-to-flame` already burns the backpack slot and pays **fixed Pale Marks by rarity** (keep these numbers):

| Rarity | Pale Marks (per item) |
|--------|------------------------|
| Common | 5 |
| Uncommon | 15 |
| Rare | 40 |
| Epic | 100 |

On **every** successful discard, also roll **1% chance to award 1 Cryptol** (always 1, never scaled by rarity). Flavor drip, not a farm — Cryptol remains a bought currency (see Dim Carcosa below).

Slice work:

- **P1-A1** Keep F-anywhere. Remove or ignore the “nearby altar” prompt; `WorldObjects` stays empty. Do **not** add an altar POI.
- **P1-A2** Inventory: Offer to the Flame on a selected item (same endpoint).
- **P1-A3** HUD: Pale Marks always visible; Cryptol visible (at least when > 0, or always for dev).
- **P1-A4** Offer response includes `{ paleMarksGained, cryptolGained: 0|1 }` and a short toast if Cryptol dropped (“A pale coin in the ash.”).
- Pale Marks stay the common sink/score. What they buy in the overworld is still P2 (respec, etc.). Cryptol is **not** spent at the Flame.

---

## P0 — Chat unresponsive after heavy use

### Current

`OverworldChat` polls `GET /api/p2p/chat/messages?since=` every **500ms**, appends results, keys rows by **array index**, no `messageId` dedup.

### Likely bugs (in code, matches “worked then died”)

1. **Overlapping polls.** If a poll takes >500ms, two requests share the same `since`, both append the same messages. The log balloons, React re-renders 20 index-keyed rows, scroll fights auto-scroll. Feels frozen.
2. **Focus desync.** Window `Enter` sets `isFocused = true` and calls `input.focus()`. If focus fails (covered by another panel), state says focused but the DOM is not. Movement is gated on `chatFocused`; window Enter will **not** retry because it thinks chat is already open. ESC in `OverworldView` **returns immediately** while `chatFocused`, so you cannot even open pause to recover.
3. **No close control.** No X on the chat panel. Unfocused container uses `pointerEvents: none` (except the input row), so the log cannot be clicked to recover.
4. **Duplicate timestamps.** `SendChatAsync` stamps the wire message and the local store with two separate `UtcNow` calls. Harmless until two messages share a millisecond; `Timestamp > since` can drop one.
5. **Party / Glyph HUD overlap.** Party list and `P2POverlay` both sit `top: 12; right: 12`. Clicks and focus steal.

### Fix

- Dedup on `messageId` (already on the payload).
- Single in-flight poll (or abort previous).
- React `key={messageId}`.
- Enter focuses only if `document.activeElement` is not the input; if state says focused but input is blurred, reset.
- ESC closes chat first (register chat on `ui-stack`), then other panels.
- Add an X / “press ESC to close chat”.
- Cap rendered history at 50; drop from the front.

Nearby (`/n`) already filters to 15 tiles on receive. Party (`/p`) is not filtered to party members on send — **P1** to restrict it.

---

## P1 — Party: leave, options, UX

### What exists

- Click another player → `POST /api/p2p/party/invite`
- Invite toast: Accept / Decline
- Top-right name list with leader star
- Backend: `POST /api/p2p/party/leave`, leader invite-only, max 8, leave broadcasts, leader failover by lowest peer id

### What is missing

| Gap | Notes |
|-----|--------|
| **Leave party** | No button. API is ready. |
| **Party options panel** | No kick, promote, disband, “enter dungeon as party”. Clicking a name only invites. |
| **Inspect** | `ui-stack` has `'inspect'` but nothing opens it. |
| **Party chat restriction** | `/p` is a color tag, not membership-gated. |
| **Dungeon pull** | Non-leaders wait for `dungeon_start`; UI does not say “waiting for leader”. |
| **Invite while already in a party** | Leader-only; no error toast if invite fails. |

### Desired slice UX

- Party panel: member list, **Leave**, leader **Kick**.
- Right-click (or click) a player: Invite / Whisper / (later) Friend.
- ESC closes the party panel like every other window.
- When a party member enters a dungeon, others get a prompt: “X entered The Drowned Dock — Join”.

---

## P1 — Windows, ESC, close buttons

`ui-stack.ts` describes LIFO ESC. `OverworldView` **does not use** `handleEscape()`; it has a hardcoded if/else and **never lists Glyph or chat**.

| Window | X button | ESC | On `ui-stack` | Notes |
|--------|----------|-----|---------------|--------|
| Pause | Resume | Toggles (buggy if chat focused) | yes | |
| Settings | ✕ | yes | yes | |
| Inventory | ? | yes | yes | Check explicit X |
| Flame offering | close | yes | yes | |
| Ability select | Cancel | yes | yes | Loadout is on **I**; do not add an altar to open this |
| Glyph panel | **No** (only “Hide Glyph”) | **No** | **No** | ESC in overlay only dismisses admin broadcast |
| Chat | **No** | Only if input focused | **No** | |
| Party invite toast | Decline | **No** | **No** | |
| Inspect | — | — | listed, unused | |

**Fix:** every overlay registers on the stack; ESC always pops the top; every overlay has an X. Glyph is a first-class panel.

---

## P1 — Settings that a top-down online RPG actually needs

### Already in ESC → Settings

- Display name  
- Offline mode (skip tracker)  
- Master volume **stub** (slider saves, no audio mixer)  
- Show Glyph overlay  
- Show FPS (works: `FpsMeter` in `OverworldView`)

### Missing for the slice

| Setting | Default | Why |
|---------|---------|-----|
| **Show connected players** | off | Requested. Show `peerCount + 1` (or shard `playerCount`) on HUD when enabled. Peer badge already exists in `P2POverlay` but is not a setting and always-on. |
| **Show names / health over players** | on | Crowded shards need a toggle. |
| **Show ability aim / AoE preview** | on | Required once VFX land. |
| **Screen shake** | on | Boss hits, Pale Blade. |
| **Chat opacity / scale** | 100% | Accessibility after the chat bug. |
| **Mouse invert / hold-to-move** | off | Quality of life. |
| **Key rebind** (WASD, E, I, F, ESC) | defaults | Even a small rebind table. |
| **Display name vs character name** | — | Name is already here. |
| **Streamer mode** | off | Hide Glyph, hide IP-ish HUD. |
| **Friend notifications** | on | Once friends list exists. |
| Volume: SFX / music split | stub | P2, after real audio. |

FPS: implemented. Wire “show player count” the same way (`ShowFps` pattern on `PlayerSave` + settings POST).

---

## P1 — Friends list (PeerId, not name)

Names change. **Friend key is `PeerId`** (16-char hex in `peer-identity.json`). Display name is a cached label, refreshed on sight.

### Behavior

- Add friend: from player click, Glyph success, or party member.
- Persist `friends.json` beside the exe: `{ peerId, lastKnownName, addedAt, lastSeenAt }`.
- HUD list: online (in current mesh) vs offline.
- **Shard split priority (at 100):** when a world is full and a new peer must overflow, **prefer keeping friends together**. Practical slice rule:
  - If you connect via Glyph to a friend, you join **their** shard even if you would have been assigned elsewhere (already true via Glyph world index).
  - If matchmaking would place you in world N+1 but a friend is in world N with room, prefer N.
  - When N is full: you still cannot exceed 100. Friends list does not punch a hole in the cap; it only **biases assignment and reconnect**.
- Reconnect: cache bootstrap should try **friends’ last addresses before** generic `known-peers.json` order.

Do not use display name as identity anywhere in this system.

---

## P1 — Matchmaking: shard world dropdown

**Only when the tracker is reachable.** If offline / tracker down: no dropdown, current shard is locked (Glyph still works).

### Current

- `GET /api/p2p/shard` — local shard only.
- `useP2POverworld.switchShard` already POSTs `/api/p2p/shard/switch`.
- Tracker `GET /api/tracker/peers` returns everyone (dashboard). Register returns **same-world only**.

### Desired

- New tracker endpoint e.g. `GET /api/tracker/worlds` → `{ worldId, playerCount, maxPlayers=100 }[]`.
- Game polls it only if `TrackerClient.IsTrackerOnline`.
- If `worlds.length > 1`, Settings (or P2P overlay) shows a **dropdown** of shards. Selecting one calls existing `SwitchShardAsync` then tracker re-register.
- If only one world exists, hide the dropdown (do not show a useless one-item select).
- Show population `23/100` next to each option.

---

## P1 — Cryptol, Dim Carcosa, and the matchmaking shop

### What the books actually say about Lake Hali

Robert W. Chambers’ *The King in Yellow* does **not** put a ruined city *under* the lake.

- Carcosa is a city **on the shore** of Lake Hali. Cassilda’s Song: *“Along the shore the cloud waves break, / The twin suns sink behind the lake, / The shadows lengthen / In Carcosa.”*
- *The Repairer of Reputations* remembers *“when the twin suns sink into the lake of Hali.”*
- The uncanny part of Hali is the **cloud-waves** themselves — a lake that behaves like sky, that can swallow suns.

So: drowned streets beneath Hali are a **Carcosa-the-game** reading of those lines, not a Chambers map. That is fine. “Suns sink *into* Hali” + cloud-waves is enough license to reveal something that was always under the water, only visible when the wider network (matchmaking) is present.

**In-game name:** **Dim Carcosa** (already in `OVERWORLD_VISION.md`). Subtitle/tooltip: *“The cloud-waves recede.”* Do not call it “The Warehouse” or a generic “shop zone.”

Lake on the current 200×200 map is already painted: deep water ellipse around **(55, 90)**. That is the reveal site.

### Two currencies (locked in)

| Currency | How you get it | Where you spend it |
|----------|----------------|-------------------|
| **Pale Marks** | Offer to the Flame (fixed table above) | Overworld sinks later (P2). Always available offline. |
| **Cryptol** | Primarily **purchased** through the matchmaking service. Tiny 1% Flame drip (1 Cryptol). **Dev:** new players start with **1000 Cryptol**. | **Only** while connected to matchmaking, and **only** at the Stranger in Dim Carcosa. No other vendor, no other SKU. |

`CryptolStore` (`cryptol.json`, keyed by player id) already exists. Old wave-mode awards (1000 on warehouse victory, 10 on defeat) in `SessionManager` are leftover shooter economy — **do not use them** for this shop. Either stop awarding on dungeon complete or keep them disabled until the shop ships.

**Production vs dev starting balance:**

- **Dev / slice:** first time a peer id is created, `CryptolStore` grants **1000**. Lets us test a 1000-Cryptol listing without a payment pipeline.
- **Ship:** starting Cryptol is **0**. The 1000 grant is behind a clearly named flag (e.g. `Carcosa:DevStartingCryptol: 1000`) defaulting off in release `appsettings`.

Buying Cryptol for real money is matchmaking-only and out of slice scope; the shop UI can show “Acquire Cryptol” as disabled/coming soon.

### When matchmaking is up: the lake opens

**Offline / tracker down:** Lake Hali is ordinary deep water. Unwalkable. No city. No shop. Flame still works (Pale Marks + 1% Cryptol roll). You can *hold* Cryptol offline; you cannot *spend* it.

**Tracker reachable:** the cloud-waves part. Deep-water tiles in the Hali ellipse become a **ruined city overlay**:

- Walkable drowned streets, empty ruined stone shells, broken colonnades, no combat required for the slice.
- Lots of buildings you **cannot** enter (collision + maybe a “sealed” prompt).
- **One** enterable structure at the center of the lake (~55, 90): **The Last House** / **The Pallid Exchange** (pick one name and stick to it — recommend **The Last House**).

This overlay is **the same geography for every shard** (baked into the overworld, visibility gated on `TrackerClient.IsTrackerOnline`). It is not a random dungeon map.

### The Last House (instanced, not random)

Entering follows **the same instance rules as a dungeon** (vanish from overworld, `in_dungeon`-style status, exit returns you to the door on the lake street) but:

- **Fixed layout.** No seed roll. Same rooms every time, every player.
- No trash pack / elites / boss for the slice (can add atmosphere NPCs later).
- Exit portal back to Dim Carcosa streets, not the fishing village.

Inside: one strange NPC — **The Stranger** (Pallid Mask energy; does not give a true name). They trade **Cryptol only**.

### Shop rules

- Catalog comes from the **matchmaking service** (so every connected player sees the same rotation). Local exe does not invent listings.
- Rotation: matchmaking holds a small set of SKUs and a `rotationId` / expiry (e.g. daily or weekly). Dashboard can change the listing.
- **These SKUs are the only items in the game that cost Cryptol.** They do not drop from enemies, Flame, or Pale Mark vendors.
- They are otherwise **unobtainable** (unique item ids, not in `ItemRegistry` drop tables).
- Prices are high. Slice listing example: **one item at 1000 Cryptol** (exactly the dev starting stack — one purchase, then you are broke unless you drip 1s from the Flame or buy more later).
- Cannot open the shop / cannot complete a buy if matchmaking drops mid-trade. Fail closed; do not charge locally.
- Spend: `CryptolStore` deducts only after matchmaking accepts the purchase (or, for the slice, after a successful `POST /api/matchmaking/shop/buy` round-trip).

### Slice implementation sketch

1. Gate Hali city tiles on tracker online (poll already exists via `MatchmakingClient` / `TrackerClient`).
2. Fixed interior map for The Last House (new scenario key e.g. `last_house`, not in the random dungeon roster).
3. Matchmaking: `GET /api/shop/catalog`, `POST /api/shop/buy`. Dev catalog: one 1000-Cryptol unique.
4. HUD Cryptol + Flame 1% roll.
5. `DevStartingCryptol = 1000` in development appsettings only.

---

## P1 — Combat VFX (placeholders are enough)

Overworld combat **has no VFX**. `VisualEffectsSystem` (slash arc, muzzle flash, impact spark) is only wired in **dungeon** `GameCanvas` / `page.tsx`. `useOverworldCombat` POSTs abilities with no local effect.

| Ability | Type in code | Works mechanically? | Slice VFX |
|---------|----------------|---------------------|-----------|
| **Pale Blade** / Bone Cleaver | Melee | Yes (`ExecuteMelee`) | Placeholder **slash arc** in aim direction (reuse `addSlashArc`) |
| **Ember Spray** | RangedAoE (cone of projectiles) | Yes | Cone / spray particles; optional ground decal |
| **Void Bolt** / Hex Dart | RangedSingle | Yes | Projectile already synced; add impact spark on hit |
| **Warding Light** / Grim Howl | HealAoE | Yes — heals self + allies in radius | Expanding **circle from player to `AreaRadius`**, then fade. On each healed peer: **small red arrows up** + floating **`+X HP`** |
| **Iron Veil** / Cinder Ward | Shield | Yes (`ShieldHp` on stamina bar) | Brief ring / mist around caster |
| **Shadow Step** | Mobility | Yes | Afterimage / dissolve |

### AoE preview (requested)

While holding RMB on a HealAoE / RangedAoE, show a **ghost circle** (heal) or **ghost cone** (ember) at max range. On cast, play the expanding circle then dissipate (~400–600ms). Client-side only.

### Group heal confirmation

`CombatSystem.ExecuteHealAoE` already heals every allied player in radius **including self**. Missing: a combat event the frontend can render (`heal` with `targetId`, `amount`, `x`, `y`). Add that payload and the arrows / `+X HP`.

---

## P2 — Other slice items that make it feel like a game

- Death / respawn on overworld (currently unclear vs dungeon spectate).
- Pickup prompt for ground loot (`E` vs walk-over).
- Damage numbers on enemies (white) vs players (red).
- Elite/boss telegraph (simple filled circle before slam).
- Minimap or compass toward village / last entrance.
- Footstep / hit / heal audio stubs (even one sine beep).
- “You are the shard host” debug line in settings (helps testers).
- Loading tooltip on dungeon enter (“The dock exhales…”).
- Inventory explicit X if missing; don’t allow equip swaps in dungeon (`loadoutLocked` already exists).
- Ability swaps stay on **I** (Inventory). No altar interaction.
- Party HP bars on overworld (dungeon HUD has them; overworld party list is names only).
- Confirm the 6th primary/secondary in `AbilityRegistry` match the UI lists (`bone_cleaver`, `hex_dart`, `grim_howl`, `cinder_ward`).

---

## PARKED — Glyph / NAT (successful test; do not start these now)

Hold from the architecture review unless a new live failure appears:

- PEX / handshake / tracker advertising TCP listen port instead of STUN-mapped UDP
- Immediate PEX on peer join (30s delay)
- TURN relay
- UDP reliability layer
- Glyph world index `% 36`
- Handshake vs `WorldShard` 100-cap off-by-one
- IPv6 glyphs
- Full-mesh bandwidth at 100 players (nearby-only sync)

Keep the Glyph **UI close/ESC** work in P1 (that is UX, not NAT).

---

## Suggested build order (vertical slice)

1. **P0-D1** — Solo dungeon actually loads (same process, correct scenario).  
2. **P0-D2 + P0-D3** — Exit portal + quit-in-dungeon resume at the door.  
3. **P0-D4** — Rename Warehouse → The Drowned Dock.  
4. **P0 chat** — Dedup, focus, ESC, X.  
5. **P1 windows** — Glyph/chat/party on ESC stack + X.  
6. **P1 party leave** + options panel.  
7. **P1 Flame-anywhere** + Pale Marks HUD + 1% Cryptol drip on discard.  
8. **P1 VFX** — Pale Blade slash, heal circle + `+X HP`, AoE ghost preview.  
9. **P0-D5** — Elites + boss + loot tables inside the dungeon.  
10. **P1 settings** — player-count overlay; names toggle.  
11. **P1 friends list** (PeerId).  
12. **P1 tracker shard dropdown** when matchmaking is up.  
13. Party-in-dungeon polish (join prompt, shared loot).  
14. **P1 Dim Carcosa** — lake reveal when tracker online; The Last House fixed instance; Stranger shop (1000 Cryptol SKU); dev starting 1000 Cryptol.

---

## File cheat sheet

| Area | Primary files |
|------|----------------|
| Dungeon enter bug | `DungeonInstanceManager.cs`, `OverworldView.tsx`, `app/page.tsx`, `OverworldCombatSync.MarkEnteredDungeon` |
| Entrance names | `OverworldBootstrap.cs` |
| Chat | `OverworldChat.tsx`, `OverworldSync.cs` |
| Party | `MeshPartyManager.cs`, `OverworldView.tsx`, `Program.cs` `/api/p2p/party/*` |
| ESC stack | `ui-stack.ts`, `OverworldView.tsx`, `P2POverlay.tsx` |
| Settings / FPS | `SettingsPanel.tsx`, `PlayerSave.cs`, `Program.cs` settings endpoints |
| Flame / Marks / Cryptol drip | `FlameOfferingPanel.tsx`, `Program.cs` offer-to-flame, `Cryptol/CryptolStore.cs` |
| Dim Carcosa + shop | `OverworldBootstrap.cs` (Hali tiles), `TrackerClient.IsTrackerOnline`, new `last_house` instance, matchmaking `/api/shop/*` |
| Abilities | `AbilityRegistry.cs`, `CombatSystem.cs`, `AbilitySelectPanel.tsx` |
| VFX (dungeon only today) | `lib/engine/effects.ts`, `GameCanvas.tsx` |
| Friends (new) | new `FriendsStore` + overlay; persist next to `known-peers.json` |
| Shard dropdown | `TrackerClient.cs`, matchmaking `Program.cs`, `P2POverlay.tsx` / Settings |
| Loot plan | `P2P_LOOT_DISTRIBUTION_PLAN.md`, `OverworldCombatSync.cs` |
