# CARCOSA — Technical Architecture Documentation

**Version:** 1.2  
**Last Updated:** August 18, 2026  

Also see [docs/DEPENDENCY_TREE.md](docs/DEPENDENCY_TREE.md) (C# project / class graph), [docs/SPRITE_TECHNICAL.md](docs/SPRITE_TECHNICAL.md) (sprite sizes, palettes, asset pipeline), and [docs/Developer_Testing_Guide.docx](docs/Developer_Testing_Guide.docx) (clone, build, and launch-script walkthrough).

---

<img width="2067" height="1278" alt="Screenshot 2026-08-10 204840" src="https://github.com/user-attachments/assets/3e9a4162-759e-45ce-9c29-313a45640c6a" />

<img width="2060" height="692" alt="Mesh_and_dashboard" src="https://github.com/user-attachments/assets/76851111-2058-4b6c-8994-6019ddcad841" />

<img width="2193" height="762" alt="kafka_admin_messaging" src="https://github.com/user-attachments/assets/c3beb56e-9289-43f8-81f1-6098100de357" />



## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Getting Started](#2-getting-started)
3. [Technology Stack](#3-technology-stack)
4. [Project Structure](#4-project-structure)
5. [Architecture Overview](#5-architecture-overview)
6. [Current Gameplay Systems](#6-current-gameplay-systems)
7. [How .NET Serves the React Frontend](#7-how-net-serves-the-react-frontend)
8. [The P2P Mesh Network](#8-the-p2p-mesh-network)
9. [Peer Discovery & Glyph Codes](#9-peer-discovery--glyph-codes)
10. [The Matchmaking Service](#10-the-matchmaking-service)
11. [Kafka Integration](#11-kafka-integration)
12. [WebSocket Communication](#12-websocket-communication)
13. [The Game Loop & Combat System](#13-the-game-loop--combat-system)
14. [Frontend Architecture (React/Next.js)](#14-frontend-architecture-reactnextjs)
15. [Native AOT & JSON Serialization](#15-native-aot--json-serialization)
16. [Build, Deploy & Run](#16-build-deploy--run)
17. [Next Steps / TODOs](#17-next-steps--todos)
18. [Future Work / Todo](#18-future-work--todo)

---

## 1. Executive Summary

Carcosa is a **peer-to-peer (P2P) top-down action RPG** (A Link to the Past exploration + Lovecraft / King in Yellow atmosphere). Players get a complete **single-player path**. Other peers may join organically as a mesh forms. There is **no required central game server**; the world stays playable via a mesh of `Carcosa.Server.exe` processes.

**Elevator pitch:** Discover the secrets of Carcosa, retrieve ancient artifacts and wisdom that may point to a route home… or to your doom. Play with friends or entirely offline. No centralized server means the game can remain playable for all time.

**Key architectural decisions (as the code exists now, not the original sketch):**
- **Each player runs a Native AOT game server** that serves a Next.js static frontend from `wwwroot` and talks to peers over `/ws/peer` (JSON WebSocket) plus a **UDP mesh socket** used for STUN, hole-punch hellos, and fallback mesh JSON.
- **Overworld is generated in-process** (`OverworldWorldGen` / `OverworldBootstrap`, 640×640 tiles). It is not loaded from matchmaking. Combat, inventory, quest, and digging are **localhost REST** polled by React.
- **Dungeons are still a split path.** Mesh `DungeonInstanceManager` allocates an instance, then the frontend opens `/ws` into the legacy `GameLoop` wave-shooter. Loadout is copied from the overworld. This is why dungeon feel lags the overworld (see §17).
- **Quest, Key Items, Friends, and dig loot are local authority.** They persist in encrypted `player-save.dat` (save format v3) and are **never meshed**. Multiplayer is additive.
- **Friends ≠ Party.** Party is combat/loot grouping. Friends is a persisted peer-id list for a **future mesh-split** that does not exist yet: if a cluster must split, Friends should stay together.
- **Matchmaking + Kafka are optional discovery only.** The game server has **no Kafka client**. Heartbeats go REST to matchmaking; Kafka lives inside the matchmaking service. Offline mode skips tracker entirely.
- **TURN is missing.** STUN + UPnP (`UpnpPortMapper`) + Glyph + PEX exist. Symmetric NAT / CGNAT pairs still fail (see [`implementations/NAT_TURN_GAP.md`](implementations/NAT_TURN_GAP.md)).

---

## 2. Getting Started

New clone, Windows x64:

1. Install .NET 10 SDK and Node.js 18+.
2. **Run `dev_scripts/release/build_all_release.bat` first.** This publishes Native AOT `Carcosa.exe`, `Carcosa.Matchmaking.exe`, and the bot client. It takes several minutes.
3. Double-click a launcher under `dev_scripts/release/` (see the table). Press any key in the script console to stop what it started.
4. Full walkthrough with expected results and screenshot slots: [docs/Developer_Testing_Guide.docx](docs/Developer_Testing_Guide.docx).

| Script | What it is for |
|--------|----------------|
| `release/launch-two-players-local-tracker.bat` | Two localhost peers + local tracker; they auto-discover. Local testing confirmed. |
| `release/launch-two-players-local-tracker-no-cache.bat` | Same, but clears `known-peers.json` so leftover WAN IPs cannot sneak in. |
| `release/launch-two-players-no-tracker-no-cache.bat` | Two peers, no tracker, no cache. Join with Glyph sharing. Local testing confirmed. |
| `release/launch-full-test.bat` | Tracker + two peers without localhost pin (closest to production flags). |
| `develop/build_all_debug.bat` then `develop/launch-*.bat` | Same launchers against Debug builds. |

Same-machine tests pin `127.0.0.1`. Long-distance Glyph tests: run `Carcosa.exe` with **no** `--public-address` so STUN can advertise the real public IP.

---

## 3. Technology Stack

| Layer | Technology |
|-------|-----------|----------------|
| Backend Runtime | .NET 10 (Native AOT) |
| Web Framework | ASP.NET Core Minimal API |
| Real-time Communication | Raw WebSockets |
| Frontend Framework | React 18 + Next.js 15 |
| Frontend Build | Next.js Static Export |
| Message Streaming | Apache Kafka (Confluent.Kafka) |
| Serialization | System.Text.Json (source-generated) |
| Package Manager | NuGet (.NET) + npm (frontend) |
| Container | Docker + docker-compose |
| Test Framework | xUnit |

---

## 4. Project Structure

```
bbq/
├── src/
│   ├── backend/              # .NET Game Server (the main application)
│   │   ├── Game/             # Dungeon GameLoop, entities, combat, map generation
│   │   ├── Gameplay/         # Overworld, inventory, quest, dig, save, dungeons
│   │   ├── Network/          # WebSocket /ws (dungeon) + matchmaking REST client
│   │   ├── P2P/              # Mesh, Glyph, STUN/UDP, UPnP, handshake, shards
│   │   ├── Cryptol/          # Currency persistence (Pale Marks)
│   │   ├── Program.cs        # Entry point, DI, all REST endpoints (AOT JSON)
│   │   ├── wwwroot/          # Built frontend (generated, not committed)
│   │   └── Carcosa.Server.csproj
│   ├── frontend/             # Next.js React SPA
│   │   ├── app/              # Next.js App Router (pages)
│   │   ├── components/       # React components (canvas, HUD, panels)
│   │   ├── hooks/            # Custom hooks (input, P2P polling, combat)
│   │   ├── lib/              # Shared utilities (map, messages, engine)
│   │   └── package.json
│   ├── matchmaking/          # Optional matchmaking + tracker service
│   │   ├── Services/         # Kafka, session registry, player store, analytics
│   │   ├── Overworld/        # Centralized overworld (legacy, being replaced by P2P)
│   │   └── Program.cs
│   ├── matchmaking-dashboard/ # Admin dashboard (separate Next.js app)
│   ├── botclient/            # Automated test bot
│   └── tests/                # xUnit test project
├── dev_scripts/
│   ├── release/              # Native AOT publish + launchers (start here)
│   ├── develop/              # Debug build + the same launchers
│   ├── python/               # Art pipeline (palettes / sprites / tilesets)
│   └── obsolete_or_fix/      # Old per-project builders; ignore
├── docs/                     # Current technical notes + Developer Testing Guide.docx
├── implementations/          # Plans and future-work writeups
├── backlog/                  # Ticket-sized remaining work
├── docker-compose.yml        # Kafka + Matchmaking local setup
└── Carcosa.slnx              # .NET solution file
```

---

## 5. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────┐
│                        PLAYER'S MACHINE                              │
│                                                                     │
│  ┌──────────────────────────────────────────────────────────────┐  │
│  │  Carcosa.Server.exe (single binary)                          │  │
│  │                                                              │  │
│  │  ┌─────────────┐  ┌────────────────┐  ┌─────────────────┐  │  │
│  │  │  ASP.NET    │  │  Game Loop     │  │  P2P Mesh       │  │  │
│  │  │  (REST +    │  │  (20Hz tick)   │  │  (WebSocket     │  │  │
│  │  │  static     │  │  Entity update │  │   connections   │  │  │
│  │  │  files)     │  │  Combat        │  │   to other      │  │  │
│  │  │             │  │  AI            │  │   players)      │  │  │
│  │  └──────┬──────┘  └───────┬────────┘  └────────┬────────┘  │  │
│  │         │                  │                     │           │  │
│  │         │  HTTP localhost   │  Internal calls     │ WebSocket │  │
│  │         ▼                  ▼                     ▼           │  │
│  │  ┌──────────────────────────────────────────────────────┐   │  │
│  │  │              wwwroot/ (Next.js static export)         │   │  │
│  │  │  React frontend polls REST endpoints at 10-16Hz      │   │  │
│  │  │  Canvas renders game state                            │   │  │
│  │  └──────────────────────────────────────────────────────┘   │  │
│  └──────────────────────────────────────────────────────────────┘  │
│         ▲                                           │               │
│         │ Edge App Mode (no browser chrome)         │               │
│         └───────────── Browser Window ──────────────┘               │
└─────────────────────────────────────────────────────────────────────┘
         │                                    │
         │ WebSocket (ws://peer:5000/ws/peer) │
         ▼                                    ▼
┌──────────────────┐              ┌──────────────────┐
│  Player B's      │◄────────────►│  Player C's      │
│  Carcosa.Server  │   P2P Mesh   │  Carcosa.Server  │
└──────────────────┘              └──────────────────┘

         ┌───────── Optional (not required for gameplay) ─────────┐
         │                                                         │
         ▼                                                         │
┌──────────────────────────────────────────────────────────┐      │
│  Matchmaking Service (docker-compose)                     │      │
│  ┌──────────┐  ┌──────────────┐  ┌────────────────────┐ │      │
│  │  REST API │  │ Kafka Consumer│  │ P2P Tracker       │ │      │
│  │  :5100    │  │ sessions.active│  │ (peer discovery) │ │      │
│  └──────────┘  └──────────────┘  └────────────────────┘ │      │
│                        ▲                                  │      │
└────────────────────────│──────────────────────────────────┘      │
                         │                                         │
                  ┌──────┴──────┐                                  │
                  │ Apache Kafka │ ◄────────────────────────────────┘
                  │ (KRaft mode) │   Session Heartbeats
                  └─────────────┘
```

**Communication Patterns (current):**
1. **Frontend → local backend:** REST polling over localhost (10–20Hz). Overworld combat, inventory, quest, friends, dig, fog, settings.
2. **Frontend → local backend (dungeon only):** WebSocket `/ws` into `GameLoop` (20Hz input / state).
3. **Peer → Peer:** WebSocket `/ws/peer` full mesh (20Hz state). UDP on the same listen port for STUN, hole-punch, optional mesh JSON.
4. **Game Server → Matchmaking (optional):** REST heartbeats every 10s via `MatchmakingClient`. **No Kafka in the game server.**
5. **Matchmaking → Kafka (optional):** Matchmaking service consumes session heartbeats into its registry.

---

## 6. Current Gameplay Systems

This is the evolved runtime, not the original design sketch.

### Dual play surfaces

| Surface | Authority | Transport | Feel |
|---------|-----------|-----------|------|
| **Overworld** | Local `OverworldCombatSync` + shard host for enemies | REST (`/api/gameplay/*`, `/api/p2p/*`) | LTTP-style exploration, RMB abilities, inventory, NPCs, fog-of-war |
| **Dungeon** | Local `GameLoop` + `SessionManager` | WebSocket `/ws` after `POST /api/gameplay/dungeon/enter` | Still a wave-shooter map from `MapGenerator`. Loadout is copied from overworld. |

The overworld map is **in-EXE** (`OverworldWorldGen`, 640×640). Matchmaking's `Overworld/` folder is a **legacy dashboard path**, not the live world.

**Shard host:** lowest peer id runs overworld enemy AI (`ShardHost`). Each peer is authoritative for **their own** player position.

**Fog of war** is packed into the save file and is **not** P2P. Other players do not share exploration.

### Save file (v3)

`SaveManager` writes encrypted `player-save.dat` (AES-256-CBC + HMAC, PBKDF2 from peer id). Auto-save ~60s and on shutdown.

v3 added (local only, never meshed):
- `KeyItemIds`, `Friends` (`SavedFriend`: peer id + display name)
- Necronomicon quest stage, functions, rank
- `DefeatedDungeonIds`, `CollectedWorldObjectIds`, `DugSpotIds`
- See Beyond marker (area id, x/y, label, active)

**AOT rule:** every new HTTP DTO must be listed on `AppJsonContext` in `Program.cs`. Save types go on `PlayerSaveJsonContext`. P2P types go on `PeerJsonContext`. Forgetting this fails silently in Release/AOT.

### Key Items & Necronomicon quest (implemented)

Key Items are **not backpack**. Screen: **K**, or Pause is for Friends. Inventory footer points at K.

Early chain (local `QuestProgression`):

1. Wash ashore near **Merek** and the Dream Hull (`dream_ship`).
2. Pick up **Old Book Husk** (`old_book_husk`, wet sand ~0.545, 0.955) with **E** → `POST /api/gameplay/world-pickup`.
3. Talk to Merek (`POST /api/gameplay/npc-talk`) — he binds the husk into a blank **Necronomicon** (0 functions) and sends the player to the **Drowned Docks**.
4. Defeat the docks boss (`boss_warehouse` in the wave loop). Victory must be reported (`dungeon/complete` with `victory: true` and `SessionManager.EndGame(true)`). Pages bind; Necronomicon gains **See Beyond**.
5. Use Necronomicon from Key Items (`POST /api/gameplay/key-items/use`). A **pulsing map marker** is planted for the suggested next area. It **ignores fog-of-war**. Pulse stops when that area's boss is defeated. The player may ignore it.
6. Later bosses raise `NecronomiconRank` and re-using See Beyond plants a new marker.

Suggested chain (coordinates match dungeon entrances; content gaps noted in §17): drowned_dock → Temple of Hali → Mountain Cave → Sunken Cyclopean Quay → Palace Crypt.

Quest APIs: `GET /api/gameplay/quest`, `POST /api/gameplay/npc-talk`, `POST /api/gameplay/world-pickup`, `POST /api/gameplay/key-items/use`.

### Friends (implemented; mesh-split not implemented)

Pause menu → **Friends**, or the friends panel lists connected `/ws/peer` peers. Toggle persists to the save file (`GET/POST /api/gameplay/friends`).

**Rationale:** if a mesh cluster ever needs to split (bounded neighborhood / overflow), **prefer keeping Friends in the same neighborhood**. Do not use Friends for combat authority or loot rights (party already covers that). The split algorithm is future work.

### Digging (framework implemented; shovel not granted yet)

A Link to the Past style. **G** on sand / path / desert / ash / dark grass / grass. Requires Key Item `obsidian_shovel` (not awarded by the early quest — later dungeon / Temple of Hali is the intended grant).

`POST /api/gameplay/dig` → `DigSystem.TryDig`. Most tiles yield nothing or minor junk (`dark_feathers`, `raw_gronk_meat`). Twelve named artifacts sit on deterministic world coordinates (three are secrets at map corners). Majority are **passive** Key Items; passives are **catalogued, not yet wired to combat/AI**.

Dig loot is **private**. No P2P broadcast.

### Dungeon right-click (fixed)

Dungeon secondary used to open the **browser context menu** because `/ws` input only treated **E** as secondary and the canvas did not `preventDefault` on `contextmenu`. Now:

- `lib/engine/input.ts`: RMB + `contextmenu` prevent; `secondaryAbility = rightMouseDown || E`
- `GameCanvas.tsx` and `GameHUD.tsx` also block the context menu

Overworld already did this on `OverworldCanvas`.

### What is deliberately not synced

Quest flags, Key Items, Friends, dig spots, fog-of-war, settings, inventory, Pale Marks. Two players in the same shard can be on different quest steps. That is MMORPG-lite by design.

---

## 7. How .NET Serves the React Frontend

### The Build Pipeline

The frontend is a **Next.js static export** — no Node.js server runs at runtime. The .NET MSBuild target handles integration:

```xml
<!-- From Carcosa.Server.csproj -->
<Target Name="BuildFrontend" BeforeTargets="BeforeBuild"
        Condition="'$(SkipFrontendBuild)' != 'true'">
  <Exec Command="npm ci --prefer-offline" WorkingDirectory="..\frontend" />
  <Exec Command="npm run build" WorkingDirectory="..\frontend" />
  <RemoveDir Directories="wwwroot" />
  <MakeDir Directories="wwwroot" />
  <Copy SourceFiles="@(FrontendOutput)"
        DestinationFiles="@(FrontendOutput->'wwwroot\%(RecursiveDir)%(Filename)%(Extension)')" />
</Target>
```

**What happens at build time:**
1. `npm ci` installs frontend dependencies
2. `npm run build` triggers Next.js export (`output: 'export'` in `next.config.mjs`)
3. Next.js produces static HTML/JS/CSS in `frontend/out/`
4. The MSBuild target copies `frontend/out/**` → `backend/wwwroot/`
5. The .NET binary embeds `wwwroot/` as its static web root

**Skip for backend-only development:** `dotnet build /p:SkipFrontendBuild=true`

### How Static Files Are Served at Runtime

```csharp
// Program.cs — Static file serving setup
builder.Environment.ContentRootPath = AppContext.BaseDirectory;
builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

// Later, after building the app:
app.UseDefaultFiles();   // Serves index.html for "/" requests
app.UseStaticFiles();    // Serves all files in wwwroot/

// SPA fallback — any non-API, non-file request returns index.html
app.MapFallback(async context =>
{
    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    }
});
```

### Frontend-Backend Communication Model

The frontend communicates with its LOCAL backend (same machine, same process) via HTTP:

```typescript
// Frontend polling pattern (hooks/usePlayerStats.ts)
const fetchStats = async () => {
  const res = await fetch('/api/gameplay/player-stats');  // localhost, same origin
  if (res.ok) {
    const data = await res.json();
    setStats(data);
  }
};
// Polls at 10Hz (100ms interval) for responsive combat UI
setInterval(fetchStats, 100);
```

**Why polling instead of WebSocket for frontend:**
- The frontend talks to its OWN local server (zero network latency)
- REST polling at 10Hz over localhost is effectively instant (~1ms)
- Simpler than maintaining a WebSocket connection for one-directional data
- The "real" WebSocket connections are between PEERS (server-to-server)

---

## 8. The P2P Mesh Network

### Concept

Every player runs their own instance of `Carcosa.Server.exe`. These instances connect to each other via WebSocket (`/ws/peer`) forming a **full mesh**. There is NO central game server. A **UDP socket on the same listen port** (`UdpMeshTransport`) runs STUN and hole-punch hellos so Glyphs can advertise a mapped UDP port; TCP `PublicAddress` remains the WebSocket fallback. **TURN is not implemented.**

### Peer Identity

Each instance has a persistent identity generated on first launch:

```csharp
// PeerIdentity.cs — persisted to peer-identity.json
public sealed class PeerIdentity
{
    public required string PeerId { get; init; }      // 16-char hex GUID
    public string DisplayName { get; set; } = "";     // Player's name
    public string WorldId { get; set; } = "";         // Current shard
    public string PublicAddress { get; set; } = "";   // IP:port for others to connect
    public int ListenPort { get; set; } = 5000;
}
```

### WebSocket Handshake Between Peers

When Peer A connects to Peer B:

```
Peer A (Initiator)                    Peer B (Listener)
    │                                      │
    │── TCP + WebSocket to /ws/peer ──────►│
    │                                      │
    │── PeerMessage{type:"handshake"} ────►│
    │   (PeerId, DisplayName, Version,     │
    │    WorldId, Capabilities)            │
    │                                      │ ← Validate:
    │                                      │   - Protocol version match?
    │                                      │   - Same world shard?
    │                                      │   - Not already connected?
    │                                      │   - Not at capacity?
    │                                      │
    │◄── PeerMessage{type:"handshake_response"}
    │    (Accepted: true, PeerId, Name)    │
    │                                      │
    │   ═══ Connection Active ═══          │
    │                                      │
    │◄──── State Updates (20Hz) ──────────►│
    │◄──── Chat Messages ─────────────────►│
    │◄──── Combat Actions ────────────────►│
    │◄──── Enemy Sync (10Hz, host only) ──►│
    │◄──── Keepalive Pings ───────────────►│
```

### The PeerMesh Class

```csharp
// PeerMesh.cs — manages all peer connections
public class PeerMesh
{
    public PeerIdentity LocalIdentity { get; }
    public int PeerCount => _peers.Count;
    public IEnumerable<string> ConnectedPeerIds => _peers.Keys;

    // Events
    public event Action<PeerConnection>? OnPeerJoined;
    public event Action<PeerConnection>? OnPeerLeft;
    public event Action<PeerConnection, PeerMessage>? OnPeerMessage;

    // Send to all connected peers
    public async Task BroadcastAsync(PeerMessage message) { ... }

    // Send to specific peer
    public async Task<bool> SendToPeerAsync(string peerId, PeerMessage message) { ... }

    // Accept inbound connection (called by /ws/peer endpoint)
    public async Task HandleInboundPeerAsync(WebSocket ws, CancellationToken ct) { ... }

    // Connect outbound to another peer
    public async Task<bool> ConnectToPeerAsync(string address) { ... }
}
```

### Overworld State Sync (OverworldSync.cs)

Each peer broadcasts its own player position at 20Hz:

```csharp
// Broadcast loop — only sends when state changes (dirty flag optimization)
private async Task BroadcastLoop(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(50, ct);  // 20Hz
        if (!_localDirty) continue;
        _localDirty = false;

        var msg = new PeerMessage
        {
            Type = "state_update",
            StateUpdate = new PeerStateUpdatePayload
            {
                PeerId = _localIdentity.PeerId,
                DisplayName = _localIdentity.DisplayName,
                X = _localX, Y = _localY,
                VelocityX = _localVelocityX, VelocityY = _localVelocityY,
                Status = _localStatus,      // "exploring", "in_party", "in_dungeon"
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            }
        };
        await _mesh.BroadcastAsync(msg);
    }
}
```

**Key Design Principle:** Each peer is AUTHORITATIVE over their own player position. Other peers trust and render it. Anti-cheat validation happens post-hoc.

---

## 9. Peer Discovery & Glyph Codes

### Three Ways Peers Find Each Other

1. **Tracker Discovery (automatic)** — Peers register with an optional tracker service
2. **Glyph Codes (manual)** — Players share human-readable codes
3. **Peer Exchange (PEX)** — Connected peers share their known peer lists

### Tracker Client

```csharp
// TrackerClient.cs — registers with matchmaking's tracker API
public sealed class TrackerClient
{
    // On startup: discover our public address via STUN-like reflection
    // GET /api/tracker/reflect → returns the IP address the server sees
    
    // Register every 30 seconds (heartbeat):
    // POST /api/tracker/register
    // Body: { peerId, address, worldId, displayName, playerCount }
    // Response: list of other peers in same world → connect to each
}
```

### Glyph Codes

Glyphs encode `IP:port + world` into a memorable format:

```
Format:  WORD1-WORD2-SUFFIX
Example: HALI-DUSK-7A2F0

Encoding:
  - WORD1 = WordListA[IP_octet_0]  (256 Carcosa-themed words)
  - WORD2 = WordListB[IP_octet_1]  (256 different words)
  - SUFFIX = Base36(IP[2], IP[3], port, worldIndex)
```

**Usage flow:**
1. Player A's frontend calls `GET /api/p2p/glyph` → gets their Glyph code
2. Player A shares code with Player B (Discord, text, etc.)
3. Player B enters code in the UI → `POST /api/p2p/glyph/connect`
4. Backend decodes Glyph to IP:port → `PeerMesh.ConnectToPeerAsync(address)`
5. WebSocket handshake completes → peers are now connected

### Peer Exchange (PEX)

Every 30 seconds, each peer sends its known peer list to all connections:

```csharp
// PeerExchange.cs — share known peers for mesh self-discovery
var msg = new PeerMessage
{
    Type = "peer_exchange",
    PeerExchange = new PeerExchangePayload
    {
        Peers = mesh.GetPeerEndpoints()  // [{PeerId, Address, WorldId}]
    }
};
await mesh.BroadcastAsync(msg);
```

When a peer receives PEX data, it connects to any unknown peers. This ensures the mesh converges to full connectivity within ~2 exchange cycles.

---

## 10. The Matchmaking Service

### Overview

The matchmaking service (`src/matchmaking/`) is an **optional** ASP.NET Core REST API that provides:
- Player registration and currency management
- Session discovery (find active games)
- P2P tracker (peer discovery without sharing IP manually)
- Admin broadcast (send messages to all players)
- Analytics and dashboard

### Key Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `POST /api/register` | POST | Create new player (generates UUID) |
| `GET /api/sessions` | GET | List active game sessions |
| `GET /api/sessions/best` | GET | Best session for invader matchmaking |
| `POST /api/sessions/heartbeat` | POST | Register/update a game session |
| `POST /api/tracker/register` | POST | P2P peer discovery registration |
| `GET /api/tracker/reflect` | GET | STUN-like IP reflection |
| `POST /api/admin/broadcast` | POST | Send admin message to all peers |
| `GET /api/health` | GET | Service health check |

### Session Heartbeat Flow

```
Game Server                     Matchmaking Service
    │                                    │
    │── GET /api/health ────────────────►│  (check if online)
    │◄── 200 OK ─────────────────────────│
    │                                    │
    │  ┌─── Every 10 seconds ──────────┐ │
    │  │                               │ │
    │  │── POST /api/sessions/heartbeat►│ │
    │  │   { sessionId, hostAddress,   │ │ → updates SessionRegistry
    │  │     playerCount, state,       │ │   (stale after 30s without heartbeat)
    │  │     scenario, currentWave }   │ │
    │  │                               │ │
    │  └───────────────────────────────┘ │
    │                                    │
```

### Graceful Degradation

If the matchmaking service is unavailable:
- Game still works via Glyph codes and cached peers
- Heartbeat loop pauses and retries every 5 seconds
- No features break — matchmaking is purely for convenience

---

## 11. Kafka Integration

### Purpose

Kafka is an **optional durable event bus for the matchmaking service only**. The game server (`Carcosa.Server`) has **no Kafka package and no producer**. Players discover sessions via REST heartbeats to matchmaking (`MatchmakingClient`). Matchmaking may then persist those heartbeats on Kafka topic `sessions.active`.

If Kafka is down, matchmaking can still keep an in-memory registry (graceful degrade). If matchmaking is down, Glyph + PEX + `known-peers.json` still form a mesh.

### Topic: `sessions.active`

| Field | Description |
|-------|-------------|
| Key | `SessionId` (string) |
| Value | JSON `SessionHeartbeat` |
| Retention | 30 seconds (ephemeral — sessions are live data) |

### Producer (removed from game server)

Older docs showed `Carcosa.Server` publishing Kafka heartbeats. That path is gone. Heartbeats are HTTP from `MatchmakingClient` to matchmaking; **only** `src/matchmaking/Services/KafkaService.cs` talks to Kafka.

### Consumer (Matchmaking reads heartbeats)

```csharp
// KafkaService.cs — background consumer loop
public void ConsumeSessionHeartbeats(SessionRegistry registry)
{
    using var consumer = new ConsumerBuilder<string, string>(_consumerConfig).Build();
    consumer.Subscribe("sessions.active");

    while (true)
    {
        var result = consumer.Consume(TimeSpan.FromSeconds(1));
        if (result == null) continue;

        var heartbeat = JsonSerializer.Deserialize(
            result.Message.Value, KafkaJsonContext.Default.SessionHeartbeat);

        if (heartbeat != null)
            registry.UpdateSession(heartbeat);  // Upserts into in-memory registry
    }
}
```

**Consumer Config:**
- `GroupId = "carcosa-matchmaking"` (single consumer group)
- `AutoOffsetReset = Latest` (only current sessions matter)
- `EnableAutoCommit = true`

### Docker Setup

```yaml
# docker-compose.yml
services:
  kafka:
    image: apache/kafka:3.7.0
    ports: ["9092:9092"]
    environment:
      KAFKA_NODE_ID: 1
      KAFKA_PROCESS_ROLES: broker,controller  # KRaft mode (no Zookeeper)
      KAFKA_LOG_RETENTION_MS: 30000           # 30s retention
      KAFKA_AUTO_CREATE_TOPICS_ENABLE: "true"
      CLUSTER_ID: "carcosa-dev-cluster-001"
```

### Graceful Degradation

If Kafka is unavailable:
1. Producer `PublishHeartbeat` catches the exception, logs a warning
2. Consumer catches startup failure, logs: "Sessions will be tracked via REST heartbeats only"
3. Game servers fall back to `POST /api/sessions/heartbeat` (direct REST to matchmaking)
4. The system works identically — Kafka is an optimization, not a requirement


---

## 12. WebSocket Communication

### Two WebSocket Endpoints

| Endpoint | Purpose | Who Connects |
|----------|---------|-------------|
| `/ws` | Player client → game server (dungeon gameplay) | Browser WebSocket | Brandon's note -- This will be moved to full mesh... right now it needs that centralized server
| `/ws/peer` | Server ↔ Server (P2P mesh) | Other Carcosa.Server instances |

### Player WebSocket (`/ws`)

Used for instanced dungeon gameplay (server-authoritative):

```csharp
// Program.cs — Player WebSocket lifecycle
app.Map("/ws", async (HttpContext context, ConnectionManager cm, SessionManager sm) =>
{
    var playerName = context.Request.Query["name"].FirstOrDefault() ?? "Unknown";
    var playerId = Guid.NewGuid().ToString("N")[..8];

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    cm.TryAddConnection(playerId, playerName, webSocket);

    // Send player their ID
    await cm.SendToAsync(playerId, new GameMessage {
        Type = "player_joined",
        PlayerJoined = new { PlayerId = playerId, PlayerName = playerName }
    });

    // Block here until disconnect — standard ASP.NET WebSocket pattern
    await cm.HandleConnectionAsync(playerId, context.RequestAborted);
});
```

### Peer WebSocket (`/ws/peer`)

Used for P2P mesh networking (peer-authoritative):

```csharp
// Program.cs — Peer mesh WebSocket
app.Map("/ws/peer", async (HttpContext context) =>
{
    var peerMesh = context.RequestServices.GetRequiredService<PeerMesh>();
    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await peerMesh.HandleInboundPeerAsync(webSocket, context.RequestAborted);
});
```

### Message Envelope Pattern

All messages (both player and peer) use a discriminated union envelope:

```csharp
// One message type with nullable payload fields (only one is set per message)
public sealed class PeerMessage
{
    public required string Type { get; init; }           // Discriminator
    public PeerHandshakePayload? Handshake { get; set; }
    public PeerStateUpdatePayload? StateUpdate { get; set; }
    public PeerCombatActionPayload? CombatAction { get; set; }
    public PeerEnemySyncPayload? EnemySync { get; set; }
    public PeerDamageEventPayload? DamageEvent { get; set; }
    public PeerChatRelayPayload? ChatRelay { get; set; }
    // ... more payload types
}
```

---

## 13. The Game Loop & Combat System

`GameLoop` is the **dungeon / wave-shooter** tick loop (WebSocket `/ws`). Overworld combat is a separate 20Hz loop in `OverworldCombatSync` driven by REST. When a dungeon starts, `SessionManager` copies the overworld loadout onto dungeon player entities so primary/secondary abilities match. Dungeon ticks now call `CombatSystem.ProcessAbility` plus stamina / i-frame processing — closer to overworld, but the map and encounter model are still `MapGenerator` + `WaveSystem`, not the overworld tile rules.

### Tick-Based Architecture

The game runs at **20 ticks per second** (50ms per tick). All timing is measured in ticks:

```csharp
// GameLoop.cs — core tick loop
public sealed class GameLoop : IDisposable
{
    public const int TickRate = 20;  // 20 Hz
    public GameState State { get; }

    private async Task TickLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(1000 / TickRate, ct);  // 50ms
            State.Tick++;

            // Process all systems in order:
            ProcessInputs();       // Apply queued player inputs
            UpdateProjectiles();   // Move projectiles, check collisions
            UpdateAI();            // Enemy behavior (wander, aggro, attack)
            UpdateCooldowns();     // Decrement ability cooldowns
            BroadcastState();      // Send delta to all clients
        }
    }
}
```

### Entity Model

All game objects share a single flat class (no inheritance):
Provides advantages for: memory management, cache efficiency, and compiler optimization
This in game design is called "Data-Oriented Design" or "Fat Object" pattern.  The 
following are reasons why it works exceptionally well for Native AOT:
1. Maximum Cache Locality (Data-Oriented Design)
            Bottleneck occurs getting data TO the cpu.
            Mitigation: when objects are a predictable size they can sit in a simple array.
                     When the CPU loads one object it automatically loads the new few objs too.
                     Loop processing through the entities becomes very fast.
2. Perfect Devirtualization
            Inheritance requires virtual methods and runtime table (vtable)
            Mitigation: with flat class every single method call is direct.  AOT compiler
                     does not have to emit any vtable lookup logic into the final machine code
3. Aggressize Method Inlining...
4. Zero casting overhead
5. Aggressive Trimming (Smaller File Size)

```csharp
public sealed class Entity
{
    // Identity
    public string Id { get; init; }
    public EntityType Type { get; init; }   // Player, Enemy, Projectile
    public string SubType { get; set; }     // "gronk", "ember_spray", etc.

    // Position
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }

    // Combat
    public int Health { get; set; }
    public float Stamina { get; set; }
    public int ShieldHP { get; set; }
    public bool HasIFrames { get; set; }
    public string? TaggedBy { get; set; }   // RuneScape style loot rights

    // State tracking
    public bool IsDirty { get; set; }       // Delta sync optimization
}
```

### Shard Host (Combat Authority)

In the overworld, one peer is elected "shard host" — they run enemy AI and resolve combat:

```csharp
// ShardHost.cs — deterministic host election
public static string? DetermineHost(IEnumerable<string> allPeerIds)
{
    // Lowest alphabetically-sorted peer ID wins.
    // All peers independently calculate the same answer — no negotiation needed.
    string? lowest = null;
    foreach (var id in allPeerIds)
        if (lowest == null || string.Compare(id, lowest, StringComparison.Ordinal) < 0)
            lowest = id;
    return lowest;
}
```

---

## 14. Frontend Architecture (React/Next.js)

### Component Hierarchy

```
OverworldView (orchestrator)
├── OverworldCanvas (2D canvas — 60fps; RMB abilities, contextmenu blocked)
├── HealthBar / StaminaBar / AbilityBar / XpBar
├── SaveIndicator
├── OverworldChat
├── P2POverlay (mesh status, Glyph, admin messages)
├── PauseMenu (ESC → Resume / Settings / Friends / Quit)
├── SettingsPanel
├── FriendsPanel (connected peers → persist Friends to save)
├── InventoryPanel (I — equipment + backpack; K opens Key Items)
├── KeyItemsPanel (Necronomicon, shovel, dug artifacts)
├── OverworldMapPanel (fog + See Beyond pulse through fog)
├── DialoguePanel (Merek quest via /api/gameplay/npc-talk)
├── FlameOfferingPanel / CryptolShopPanel
└── AbilitySelectPanel

Dungeon path (page.tsx)
├── GameHUD
│   └── GameCanvas (/ws input; RMB = secondary; contextmenu blocked)
```

### Polling Hooks (how frontend gets data)

```typescript
// hooks/useP2POverworld.ts — polls player positions (16Hz)
// Returns: players[], status, shard, glyph, updatePosition()

// hooks/usePlayerStats.ts — polls combat stats (10Hz)
// Returns: { hp, maxHp, stamina, maxStamina, primaryAbility, ... }

// hooks/useOverworldEnemies.ts — polls enemies + projectiles + loot (10Hz)
// Returns: { enemies[], projectiles[], lootDrops[] }

// hooks/useOverworldInput.ts — WASD movement with client-side prediction (20Hz)
// Returns: { position, reconcile() }
```

### Canvas Rendering Pipeline

```typescript
// OverworldCanvas.tsx — render loop (60fps via requestAnimationFrame)
const render = useCallback(() => {
  // 1. Follow local player with camera (smooth lerp)
  cameraFollow(camera, localPlayer.x, localPlayer.y, 0.12);

  // 2. Clear canvas
  ctx.fillRect(0, 0, width, height);

  // 3. Render map tiles (only visible bounds — culled by camera)
  renderMapTiles(ctx, camera, map);

  // 4. Render dungeon entrances (pulsing glow)
  renderDungeonEntrances(ctx, camera, entrances);

  // 5. Render world objects (sprites with fallback to shapes)
  renderWorldObjects(ctx, camera, objects);

  // 6. Render loot drops (colored glowing squares)
  renderLootDrops(ctx, camera, lootDrops);

  // 7. Render players + enemies (Y-sorted for depth)
  renderPlayers(ctx, camera, players);  // Gold=local, Purple=remote
  renderEnemies(ctx, camera, enemies);  // Dark brown Gronks

  // 8. Render projectiles (colored circles with glow)
  renderProjectiles(ctx, camera, projectiles);

  // 9. Vignette overlay (dark edges for atmosphere)
  renderVignette(ctx, width, height);

  requestAnimationFrame(render);
}, [dependencies]);
```

### Client-Side Prediction

```typescript
// hooks/useOverworldInput.ts — movement prediction
// 1. Read WASD keys at 20Hz
// 2. Move local player immediately (predicted position)
// 3. POST position to /api/p2p/position (server broadcasts to mesh)
// 4. Server responds with authoritative position
// 5. If mismatch > threshold: snap to server position
```

---

## 15. Native AOT & JSON Serialization

### Why Native AOT

.NET Native AOT compiles C# directly to machine code (like Go or Rust). No JIT, no runtime, no .NET installation needed on the target machine. The tradeoff: **no runtime reflection**.

### The JSON Problem

Standard `System.Text.Json` uses reflection to discover properties at runtime. Under AOT, this doesn't work. Solution: **source-generated serializers**.

```csharp
// PeerJsonContext.cs — AOT-compatible JSON for P2P messages
[JsonSerializable(typeof(PeerMessage))]
[JsonSerializable(typeof(PeerStateUpdatePayload))]
[JsonSerializable(typeof(PeerCombatActionPayload))]
[JsonSerializable(typeof(PeerEnemySyncPayload))]
[JsonSerializable(typeof(PeerEnemySyncEntry[]))]
// ... every type must be listed explicitly
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public partial class PeerJsonContext : JsonSerializerContext { }
```

**CRITICAL RULE:** When adding a new message type:
1. Define the class in `PeerMessagePayloads.cs`
2. Add a nullable property to `PeerMessage`
3. Add the type constant to `PeerMessageTypes`
4. **Register in `PeerJsonContext`** — forgetting this causes silent failures in release builds

---

## 16. Build, Deploy & Run

### Prerequisites

- .NET 10 SDK
- Node.js 18+ and npm
- Docker (for Kafka + matchmaking, optional)

### Build Commands

```powershell
# Full build (backend + frontend) from src/backend/
dotnet build

# Backend only (skip slow frontend rebuild)
dotnet build /p:SkipFrontendBuild=true

# Frontend only (from src/frontend/)
npx next build

# Release build (native AOT — produces single exe)
dotnet publish -c Release -r win-x64

# Matchmaking service
dotnet build   # from src/matchmaking/

# Run tests
dotnet test /p:SkipFrontendBuild=true   # from src/tests/
```

### Running Locally

Preferred path after a clone:

```powershell
# 1) Native AOT publish (several minutes)
dev_scripts\release\build_all_release.bat

# 2) Two localhost peers + local tracker (auto-discover)
dev_scripts\release\launch-two-players-local-tracker.bat

# Glyph-only (no tracker, no peer cache)
dev_scripts\release\launch-two-players-no-tracker-no-cache.bat
```

Debug / faster iterate:

```powershell
dev_scripts\develop\build_all_debug.bat
dev_scripts\develop\launch-two-players-local-tracker.bat
```

```powershell
# Single player from source (Debug, skips publish)
cd src/backend
dotnet run

# Optional Kafka + matchmaking via compose, then one game process
docker-compose up -d
cd src/backend
dotnet run -- --matchmaking-url=http://localhost:5100
```

Walkthrough of every launcher, expected results, and screenshot slots: [docs/Developer_Testing_Guide.docx](docs/Developer_Testing_Guide.docx). Core overworld, Glyphs, and optional tracker discovery work. Persistence and many content systems are still incomplete; see §17 and `backlog/`.

### CLI Arguments

| Argument | Default | Description |
|----------|---------|-------------|
| `--port=N` | 5000 | HTTP/WebSocket listen port |
| `--headless` | false | No browser window (server only) |
| `--name=X` | "Carcosa Server" | Player display name |
| `--spawn-bots=N` | 0 | Auto-spawn N bot players |
| `--matchmaking-url=URL` | http://localhost:5100 | Matchmaking / tracker address |
| `--public-address=IP:port` | (STUN/UPnP) | Pin Glyph + tracker address (localhost tests) |
| `--no-cache-connect` | off | Do not dial known-peers.json |
| `--clear-peer-cache` | off | Delete known-peers.json |
| `--seed=N` | (none) | Dungeon seed (instance mode) |
| `--scenario=X` | (none) | Dungeon scenario (instance mode) |

### Deployment (Distribution to Players)

```powershell
dotnet publish -c Release -r win-x64
# Produces: bin/Release/net10.0/win-x64/publish/
#   Carcosa.exe          (single native binary, ~20-30MB)
#   wwwroot/             (frontend static files)
#   appsettings.json     (configuration)
```

Players receive: the exe + wwwroot folder. Double-click to play. No installation required.

---

## 17. Next Steps / TODOs

Ordered roughly by player-facing impact. Solo must remain complete; multiplayer stays optional.

### P0 — Dungeon / overworld parity

- [ ] Dungeon movement, collision, and camera should feel like the overworld (currently `MapGenerator` + wave spawns). Exit portal / return-to-overworld still has leftover friction (`implementations/VERTICAL_SLICE_BACKLOG.md`).
- [x] Dungeon right-click no longer opens a browser context menu; RMB fires secondary (same as overworld).
- [ ] Palace Crypt and Temple of Hali currently share the `temple` scenario. See Beyond treats them carefully; they need distinct maps.
- [ ] `DungeonInstanceManager.ParseScenario` maps `"warehouse"` → Drowned Dock. `QuestProgression.NormalizeDungeonId` also folds `warehouse` into `drowned_dock`, so the “Sunken Cyclopean Quay” See Beyond stop never appears as a separate clear. Split those ids.
- [ ] Copy remaining overworld systems into dungeon: pickup prompts, Key Items (K), map, Friends.

### P1 — Content & progression

- [x] Key Items screen (K) for permanent items.
- [x] Merek / Old Book Husk / Necronomicon / See Beyond early chain.
- [ ] Award **Obsidian Shovel** from a later dungeon (suggested: Temple of Hali boss). Digging exists but returns “no shovel” until then.
- [ ] Wire dig artifact **passives** to combat/AI/stamina (they currently grant Key Items + flavor only). See artifact table in [docs/SPRITE_TECHNICAL.md](docs/SPRITE_TECHNICAL.md) / `DigSystem.Artifacts`.
- [ ] Additional Necronomicon functions after See Beyond (each boss should add a named page/ability, not only rank).
- [ ] Clue items / map fragments that mark productive dig spots on the map (most digs are empty by design).
- [ ] Replace placeholder `old_book_husk.png` (16×20) with final art.

### P1 — Mesh future (do not break solo)

- [x] Friends selection + save persistence (`SavedFriend` in v3 save).
- [ ] **Mesh-split / bounded neighborhood:** when a cluster is too large, split shards but **prefer keeping Friends together**. Algorithm does not exist yet; consume `QuestProgression.Friends` / save `Friends`. Do not use Friends for loot rights.
- [ ] TURN relay for symmetric NAT / CGNAT ([`implementations/NAT_TURN_GAP.md`](implementations/NAT_TURN_GAP.md)). UPnP already attempts a quiet map of the TCP listen port.
- [ ] IPv6 Glyphs (`GlyphCodec` is IPv4).
- [ ] Unify dungeon instances onto the mesh (`DungeonInstanceManager`) instead of dropping into local `/ws` GameLoop.

### P2 — Atmosphere & systems still missing

- [ ] Interior buildings as real maps (some houses are enterable stubs).
- [ ] Day/night or Second-Sun Lens active effect (secret dig artifact).
- [ ] Nameless City Key door (secret dig artifact has no function).
- [ ] Named palettes for reference art exist in `palettes.json` / `palettes.ts`; drop reference PNGs under `assets/references/` and re-run `dev_scripts/python/palettes/extract-palettes.py` to sample true colors.
- [ ] Audio / music pass (settings have volume; little content).
- [ ] Anti-cheat is post-hoc trust of peer position (documented); no verification yet.

### Architecture decisions to preserve

1. **No central game server.** Matchmaking is discovery only.
2. **Quest / Key Items / Friends / dig / fog stay local.** Never broadcast on `/ws/peer`.
3. **AOT JSON:** register every new DTO.
4. **Friends list is split-input, not party.** Party = combat/loot. Friends = future neighborhood preference.

Related: [docs/DEPENDENCY_TREE.md](docs/DEPENDENCY_TREE.md), [docs/SPRITE_TECHNICAL.md](docs/SPRITE_TECHNICAL.md), [implementations/NAT_TURN_GAP.md](implementations/NAT_TURN_GAP.md), [implementations/VERTICAL_SLICE_BACKLOG.md](implementations/VERTICAL_SLICE_BACKLOG.md), [backlog/README.md](backlog/README.md).

---

## 18. Future Work / Todo

Ticket-sized remaining work lives in [`backlog/`](backlog/README.md). Longer plans and diagnoses live in [`implementations/`](implementations/):

| Doc | Why it is there |
|-----|-----------------|
| [`VERTICAL_SLICE_BACKLOG.md`](implementations/VERTICAL_SLICE_BACKLOG.md) | Dungeon load/exit, Dim Carcosa, slice blockers |
| [`NAT_TURN_GAP.md`](implementations/NAT_TURN_GAP.md) | TURN / hard-NAT pairs; IPv6 Glyphs; neighborhood bound |
| [`OVERWORLD_VISION.md`](implementations/OVERWORLD_VISION.md) | Region list and art-pipeline notes for the 640x640 map |
| [`P2P_LOOT_DISTRIBUTION_PLAN.md`](implementations/P2P_LOOT_DISTRIBUTION_PLAN.md) | Loot windows, autonomous pickup, task assignment |
| [`mesh-shard-network-plan.md`](implementations/mesh-shard-network-plan.md) | Gossip / shard mesh evolution |

What already works today: solo overworld, local two-peer mesh (tracker auto-discover or Glyph), long-distance Glyph share without `--public-address`, optional matchmaking tracker, STUN + UPnP, PEX + peer cache, Key Items / See Beyond early chain.

Highest-impact gaps (same order as §17): dungeon/overworld parity, remaining quest dungeons as distinct maps, Obsidian Shovel + dig passives, Friends-biased mesh split, TURN.

---

## License

I plan to create a full commercial game so feel free to get inspired by it.  I think the idea to have an online game but utilize a peer 2 peer mesh network seems pretty novel and I wont need to worry about hosting a a server! :)
