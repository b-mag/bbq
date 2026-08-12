# CARCOSA — Technical Architecture Documentation

**Version:** 1.0  
**Last Updated:** August 2026  

---

<img width="2067" height="1278" alt="Screenshot 2026-08-10 204840" src="https://github.com/user-attachments/assets/3e9a4162-759e-45ce-9c29-313a45640c6a" />

<img width="2060" height="692" alt="Mesh_and_dashboard" src="https://github.com/user-attachments/assets/76851111-2058-4b6c-8994-6019ddcad841" />

<img width="2193" height="762" alt="kafka_admin_messaging" src="https://github.com/user-attachments/assets/c3beb56e-9289-43f8-81f1-6098100de357" />



## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Technology Stack](#2-technology-stack)
3. [Project Structure](#3-project-structure)
4. [Architecture Overview](#4-architecture-overview)
5. [How .NET Serves the React Frontend](#5-how-net-serves-the-react-frontend)
6. [The P2P Mesh Network](#6-the-p2p-mesh-network)
7. [Peer Discovery & Glyph Codes](#7-peer-discovery--glyph-codes)
8. [The Matchmaking Service](#8-the-matchmaking-service)
9. [Kafka Integration](#9-kafka-integration)
10. [WebSocket Communication](#10-websocket-communication)
11. [The Game Loop & Combat System](#11-the-game-loop--combat-system)
12. [Frontend Architecture (React/Next.js)](#12-frontend-architecture-reactnextjs)
13. [Native AOT & JSON Serialization](#13-native-aot--json-serialization)
14. [Build, Deploy & Run](#14-build-deploy--run)
15. [Key Differences: .NET vs Java](#15-key-differences-net-vs-java)

---

## 1. Executive Summary

Carcosa is a **peer-to-peer (P2P) top-down action RPG** with a shared overworld and instanced dungeons. The game is designed to work **entirely without a central server** — players connect directly to each other via WebSocket mesh networking. An optional matchmaking service aids discovery but is not required for gameplay.

**Key Architectural Decisions:**
- **P2P mesh networking** — Each player runs a game server instance that connects to others
- **.NET 10 Native AOT** — Single compiled binary, no .NET runtime installation required
- **Next.js static export** — Frontend compiled to static HTML/JS/CSS, embedded in the .NET binary's wwwroot
- **Single-exe distribution** — One file contains game server + frontend + all assets
- **Apache Kafka** — Optional event streaming for session discovery (gracefully degrades without it)

---

## 2. Technology Stack

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

## 3. Project Structure

```
bbq/
├── src/
│   ├── backend/              # .NET Game Server (the main application)
│   │   ├── Game/             # Game loop, entities, combat, map generation
│   │   ├── Gameplay/         # Phase B: stamina, abilities, loot, inventory, save
│   │   ├── Network/          # WebSocket connection management, messages
│   │   ├── P2P/              # Peer mesh networking, sync, handshake, Glyph codes
│   │   ├── Cryptol/          # Currency persistence (Pale Marks)
│   │   ├── Program.cs        # Entry point, DI registration, all REST endpoints
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
├── dev_scripts/              # Build and launch scripts (.bat + .ps1)
├── docker-compose.yml        # Kafka + Matchmaking local setup
└── Carcosa.slnx              # .NET solution file
```

---

## 4. Architecture Overview

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

**Communication Patterns:**
1. **Frontend → Backend:** REST polling over localhost (10-16Hz)
2. **Peer → Peer:** WebSocket full-mesh (20Hz state broadcasts)
3. **Game Server → Matchmaking:** REST heartbeats (every 10s)
4. **Game Server → Kafka:** Session heartbeat publication (every 10s)
5. **Matchmaking → Kafka:** Consumer reads heartbeats into session registry

---

## 5. How .NET Serves the React Frontend

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

## 6. The P2P Mesh Network

### Concept

Every player runs their own instance of `Carcosa.Server.exe`. These instances connect to each other via WebSocket forming a **full mesh** — every peer is connected to every other peer. There is NO central game server.

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

## 7. Peer Discovery & Glyph Codes

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

## 8. The Matchmaking Service

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

## 9. Kafka Integration

### Purpose

Kafka serves as a **durable event bus** for session discovery. Game servers publish heartbeats; the matchmaking service consumes them.

### Topic: `sessions.active`

| Field | Description |
|-------|-------------|
| Key | `SessionId` (string) |
| Value | JSON `SessionHeartbeat` |
| Retention | 30 seconds (ephemeral — sessions are live data) |

### Producer (Game Server → Kafka)

```csharp
// KafkaService.cs — publish a session heartbeat
public async Task PublishHeartbeat(SessionHeartbeat heartbeat)
{
    using var producer = new ProducerBuilder<string, string>(_producerConfig).Build();
    var json = JsonSerializer.Serialize(heartbeat, KafkaJsonContext.Default.SessionHeartbeat);
    await producer.ProduceAsync("sessions.active", new Message<string, string>
    {
        Key = heartbeat.SessionId,
        Value = json
    });
}
```

**Producer Config:**
- `Acks = Leader` (fast acknowledgment, session data is ephemeral)
- Bootstrap servers from CLI arg or Docker env var

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

## 10. WebSocket Communication

### Two WebSocket Endpoints

| Endpoint | Purpose | Who Connects |
|----------|---------|-------------|
| `/ws` | Player client → game server (dungeon gameplay) | Browser WebSocket |
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

## 11. The Game Loop & Combat System

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

## 12. Frontend Architecture (React/Next.js)

### Component Hierarchy

```
OverworldView (orchestrator)
├── OverworldCanvas (2D canvas renderer — 60fps)
├── HealthBar (HP display)
├── StaminaBar (stamina display)
├── AbilityBar (equipped ability icons)
├── SaveIndicator (auto-save spinner)
├── OverworldChat (chat panel)
├── P2POverlay (mesh status, Glyph, admin messages)
├── QuitMenu (ESC → quit confirmation)
├── InventoryPanel (I key → equipment + backpack)
└── AbilitySelectPanel (altar interaction → ability swap)
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

## 13. Native AOT & JSON Serialization

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

**Java analogy:** Like Jackson's `@JsonTypeInfo` + `@JsonSubTypes`, but resolved at compile time. If you forget to register a type, it simply won't serialize (no runtime error in debug, silent null in AOT release).

---

## 14. Build, Deploy & Run

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

```powershell
# Option 1: Single player (debug)
cd src/backend
dotnet run

# Option 2: Two players (testing P2P mesh)
cd dev_scripts
.\launch-two-players.ps1   # Starts two instances on ports 5000 and 5001

# Option 3: Full stack (matchmaking + Kafka + game)
docker-compose up -d       # Starts Kafka + matchmaking
cd src/backend
dotnet run -- --matchmaking-url=http://localhost:5100
```

### CLI Arguments

| Argument | Default | Description |
|----------|---------|-------------|
| `--port=N` | 5000 | HTTP/WebSocket listen port |
| `--headless` | false | No browser window (server only) |
| `--name=X` | "Carcosa Server" | Player display name |
| `--spawn-bots=N` | 0 | Auto-spawn N bot players |
| `--matchmaking-url=URL` | http://localhost:5100 | Matchmaking service address |
| `--seed=N` | (none) | Dungeon seed (instance mode) |
| `--scenario=X` | (none) | Dungeon scenario (instance mode) |

### Deployment (Distribution to Players)

```powershell
dotnet publish -c Release -r win-x64
# Produces: bin/Release/net10.0/win-x64/publish/
#   Carcosa.Server.exe   (single native binary, ~20-30MB)
#   wwwroot/             (frontend static files)
#   appsettings.json     (configuration)
```

Players receive: the exe + wwwroot folder. Double-click to play. No installation required.

---

## 15. Key Differences: .NET vs Java

| Concept | Java | .NET (this project) |
|---------|------|---------------------|
| Entry point | `public static void main(String[] args)` | Top-level statements in `Program.cs` |
| Dependency Injection | Spring `@Autowired` / `@Bean` | `builder.Services.AddSingleton<T>()` |
| Web framework | Spring Boot / Micronaut | ASP.NET Core Minimal API |
| WebSocket | `@ServerEndpoint` / Netty | `app.Map("/ws", ...)` + raw WebSocket |
| JSON | Jackson ObjectMapper | `System.Text.Json` (source-generated) |
| Async/Await | `CompletableFuture` | `async/await` (built into language) |
| Collections | `ConcurrentHashMap` | `ConcurrentDictionary` |
| Properties | Getters/Setters (lombok) | `{ get; set; }` (built into language) |
| Records | Java 16+ `record` | C# `record` (similar) |
| Null safety | `@Nullable` annotations | `?` suffix (e.g., `string?`) |
| Build tool | Maven/Gradle | MSBuild (`.csproj` XML) |
| Package manager | Maven Central | NuGet |
| Native compile | GraalVM Native Image | .NET Native AOT |
| Test | JUnit 5 + Mockito | xUnit + Moq |
| Lambda/LINQ | Streams API | LINQ (`Where`, `Select`, `ToList`) |
| Interface keyword | `interface` | `interface` (same) |
| Sealed classes | `sealed` (Java 17+) | `sealed` (similar) |

### Code Pattern Comparison

**Dependency Registration:**
```java
// Spring Boot
@Configuration
public class AppConfig {
    @Bean
    public PeerMesh peerMesh(PeerIdentity identity) {
        return new PeerMesh(identity);
    }
}
```
```csharp
// ASP.NET Core (this project)
builder.Services.AddSingleton(sp => new PeerMesh(
    sp.GetRequiredService<PeerIdentity>()));
```

**Async WebSocket handling:**
```java
// Java (Netty-style)
CompletableFuture<Void> result = channel.writeAndFlush(message);
result.thenAccept(v -> log.info("sent"));
```
```csharp
// .NET (this project)
await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
```

**REST endpoint:**
```java
// Spring Boot
@GetMapping("/api/health")
public HealthResponse health() {
    return new HealthResponse("Carcosa", "1.0.0", Instant.now());
}
```
```csharp
// ASP.NET Core Minimal API (this project)
app.MapGet("/api/health", () => new HealthResponse("Carcosa", "1.0.0", DateTime.UtcNow));
```
## License

I plan to create a full commercial game so feel free to get inspired by it.
