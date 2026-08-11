# CARCOSA

A cooperative top-down action RPG with a persistent shared overworld and instanced dungeons.
Built as .NET 10 Native AOT compiled executables with a Next.js React frontend.

## Overview

Carcosa is a 1-8 player co-op game set in a sci-fi/fantasy world inspired by Robert W. Chambers'
*The King in Yellow* and the fictional city of Carcosa. Players explore a shared persistent
overworld (Lake Hali, dark forests, ancient ruins, fishing village) and form parties to enter
instanced dungeons together.

### Architecture: Shared Overworld + Instanced Dungeons

- **Overworld**: A persistent 200x200 tile map hosted by the matchmaking service. All connected
  players are visible and can interact (chat, party invite). No combat in the overworld.
- **Dungeons**: Instanced content (The Warehouse, Temple of Hali, Mountain Cave). When a party
  enters a dungeon entrance, the party leader's game server generates the instance from a shared
  seed. All party members connect peer-to-peer to the leader's server.
- **Flow**: Connect → Overworld → Form Party → Enter Dungeon → Play → Return to Overworld

### Scenarios (Dungeon Instances)

| Scenario | Layout | Mode | Victory |
|----------|--------|------|---------|
| **The Warehouse** | BSP-generated rooms/corridors | 5 waves + boss | Defeat the boss |
| **The Temple** | Large open arena (100x100) | Endless survival | Survive as long as possible |
| **Mountain Cave** | BSP dungeon | Waves + boss | Defeat the boss |

### Classes

| Class | Weapon | Special | Med Kits | Role |
|-------|--------|---------|----------|------|
| **Gangster** | Tommy Gun (spray, 15 tile range, fast fire) | — | 1 | Area suppression |
| **Detective** | Magnum (single shot, 20 tile range, 25 dmg) | — | 3 | Precision damage |
| **Surgeon** | Dagger (melee, 8 dmg) | Group Heal (15 HP, 5 tile radius) | 0 | Support/healer |

### Enemies

| Enemy | Type | Appears | Notes |
|-------|------|---------|-------|
| **Cultist Torch** | Melee | Waves 1+ | Fast, deals fire damage |
| **Cultist Dagger** | Ranged | Waves 1+ | Throws daggers, fast cooldown |
| **Cultist Shotgun** | Ranged | Waves 3+ | 5-pellet spread, devastating |
| **Cultist Lightning** | Ranged | Waves 4+ | Passes through entities, high damage |
| **Cult Leader** | AoE | Waves 4+ | Tank, area damage, buffs nearby |
| **Boss (Warehouse)** | AoE+Summon | Wave 5 | 500 HP, summons minions |

### Cryptol Currency

| Event | Reward |
|-------|--------|
| Victory (Warehouse) | 1000 Cryptol |
| Defeat (stayed connected) | 10 Cryptol |
| Temple (per wave survived) | 10 Cryptol/wave |
| Invader kill (per co-op player) | 500 Cryptol |
| Invader final kill bonus | +500 Cryptol |

## Architecture

```
┌─────────────────────────────────────────────┐
│         Matchmaking Service (port 5100)      │
│  ├── REST API (player reg, sessions, Cryptol)│
│  ├── Kafka consumer (session heartbeats)     │
│  └── Player store (players.json)             │
├─────────────────────────────────────────────┤
│         Kafka Broker (KRaft, port 9092)      │
│  └── Topic: sessions.active                  │
└─────────────────────────────────────────────┘
         ▲                           ▲
         │ heartbeats                │ session discovery
         │                           │
┌─────────────────────────────────────────────┐
│         Carcosa.Server.exe (port 5000)       │
├─────────────────────────────────────────────┤
│  Kestrel HTTP + WebSocket Server             │
│  ├── Static file serving (wwwroot/)          │
│  ├── /ws WebSocket endpoint                  │
│  └── /api/* REST endpoints                   │
├─────────────────────────────────────────────┤
│  Game Engine (20 tick/sec)                   │
│  ├── Entity System                           │
│  ├── BSP / Temple Map Generator              │
│  ├── Combat System (per-class weapons)       │
│  ├── AI System (state machine, 6 enemy types)│
│  ├── Wave System (finite or endless)         │
│  ├── Game Flow (death/spectate/game-over)    │
│  ├── Session Manager (lobby/invader)         │
│  └── Cryptol Store (local JSON)              │
├─────────────────────────────────────────────┤
│  Next.js Frontend (static export)            │
│  ├── Canvas 2D Renderer + Visual Effects     │
│  ├── Client-side Prediction                  │
│  ├── Entity Interpolation                    │
│  ├── Zoom Camera System                      │
│  ├── Pre-defined Chat (11 messages)          │
│  └── Death/Spectate System                   │
├─────────────────────────────────────────────┤
│  Native Window (WebView2Aot)                 │
│  └── Chromium game client in Win32 window    │
└─────────────────────────────────────────────┘
```

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/)
- [Docker](https://www.docker.com/) (for matchmaking/Kafka)
- Windows x64 (for the default publish target)
- Microsoft Edge WebView2 Runtime (pre-installed on Windows 10/11)

### Game Server (Development)

```bash
cd bbq/src/backend
dotnet build
```

This automatically builds the Next.js frontend and copies it to `wwwroot/`.

### Game Server (Production AOT)

```bash
cd bbq/src/backend
dotnet publish -c Release -r win-x64
```

### Matchmaking Service

```bash
# Local development with Docker
cd bbq
docker-compose up -d

# Or build standalone
cd bbq/src/matchmaking
dotnet publish -c Release -r win-x64
```

### Bot Client

```bash
cd bbq/src/botclient
dotnet build
```

## Running

### Quick Start (Solo with Bots)

```bash
# Start with 2 bot players — launches in a native desktop window
./Carcosa.Server.exe --spawn-bots=2
```

The game opens in its own window (no browser needed). Select a class, ready up, and start.

To run as a headless server (browser-based, for dedicated hosting):
```bash
./Carcosa.Server.exe --headless --spawn-bots=2
# Then open http://localhost:5000 in a browser
```

### Full Multiplayer Setup

```bash
# 1. Start matchmaking + Kafka
cd bbq && docker-compose up -d

# 2. Start game server
./Carcosa.Server.exe --port=5000

# 3. Players connect via browser to http://<host-ip>:5000

# 4. (Optional) Start bot clients
./Carcosa.BotClient.exe --server=ws://localhost:5000 --count=2
```

### Command-Line Options

**Game Server:**
```
--port=<port>        Listening port (default: 5000)
--headless           Server-only mode (no browser needed)
--spawn-bots=N       Spawn N bot players on startup
--help               Show help
```

**Bot Client:**
```
--server=<url>       WebSocket server URL (default: ws://localhost:5000)
--count=<n>          Number of bots (default: 1)
--names=<a,b,c>      Bot names (default: Bot_1, Bot_2, ...)
--help               Show help
```

**Matchmaking Service:**
```
--port=<port>        Listening port (default: 5100)
--kafka=<broker>     Kafka broker address (default: localhost:9092)
--headless           Run without dashboard window (API only)
--help               Show help
```

## Controls

| Key | Action |
|-----|--------|
| WASD / Arrows | Move |
| Left Click (on canvas) | Primary Attack |
| Spacebar | Primary Attack (alternative) |
| E | Special Ability (Surgeon: Group Heal) |
| F | Revive downed ally (hold) |
| H | Use Med Kit (full heal) |
| Enter | Open/close chat selector |
| 1-9, 0, - | Select chat message (when open) |
| Tab | Cycle spectate target (when dead) |
| Mouse Scroll | Zoom in/out |
| Escape | Close chat selector |

## Features

### Combat & Enemies
- 6 distinct enemy types with progressive difficulty
- Boss fight (Warehouse) with AoE + minion summoning
- Per-class weapon visuals (bullet trails, muzzle flash, slash arcs)
- Lightning bolts pass through multiple entities

### Multiplayer
- 1-8 player co-op (peer-hosted)
- PvP Invader mode (join mid-game as hostile)
- Pre-defined chat (11 messages, no free text)
- Headless bot clients for testing

### Game Systems
- Two scenarios (The Warehouse, The Temple)
- Cryptol currency with local JSON persistence
- Med kit system (class-based starting counts)
- Death markers + spectate mode (Tab to cycle)
- Mouse scroll zoom (0.5x to 2.5x)

### Technical
- .NET 10 Native AOT (single binary, no runtime needed)
- Native desktop windows via WebView2Aot (no browser needed)
- Source-generated JSON serialization (zero reflection)
- 20Hz server tick with 60fps client interpolation
- Client-side prediction with server reconciliation
- Delta state broadcasting (dirty entities only)
- Kafka-based session discovery via matchmaking service

### Matchmaking Dashboard
- Dark-themed admin panel (React/Next.js)
- Real-time session monitoring (polls every 3s)
- Sortable/filterable data tables with state badges
- Click-to-inspect session modal with progress bars
- Class popularity bar chart (pure CSS)
- Scenario distribution donut chart (SVG)
- Player registry with Cryptol balance tracking
- Cryptol economy overview with aggregate stats
- Live activity feed
- Launches in its own native desktop window

## Project Structure

```
bbq/
├── docker-compose.yml        # Kafka + Matchmaking (local dev)
├── README.md
├── src/
│   ├── backend/              # .NET 10 AOT game server
│   │   ├── Cryptol/          # Currency persistence
│   │   ├── Game/             # Game logic (loop, entities, combat, AI, maps, waves)
│   │   ├── Network/          # WebSocket, messages, JSON contexts
│   │   ├── GameWindow.cs     # Native Win32 window (WebView2Aot)
│   │   └── Program.cs        # Entry point & HTTP pipeline
│   ├── frontend/             # Next.js React game client
│   │   ├── app/              # Pages & layout
│   │   ├── components/       # GameCanvas, GameHUD, Lobby
│   │   ├── hooks/            # useWebSocket, useGameInput
│   │   └── lib/              # Engine (camera, renderer, input, prediction, effects)
│   ├── matchmaking/          # Matchmaking + analytics service
│   │   ├── Services/         # PlayerStore, SessionRegistry, KafkaService, AnalyticsService
│   │   ├── DashboardWindow.cs # Native Win32 window for admin dashboard
│   │   ├── Dockerfile
│   │   └── Program.cs        # REST API entry point
│   ├── matchmaking-dashboard/ # React admin dashboard
│   │   ├── app/              # Page & layout
│   │   ├── components/       # Sidebar, StatCards, SessionsTable, Modal, Charts
│   │   └── lib/              # API client
│   └── botclient/            # Headless bot test client
│       └── Program.cs        # Bot AI + WebSocket client
```

## License

This project is for educational/demonstration purposes.
