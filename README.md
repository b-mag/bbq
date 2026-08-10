# CARCOSA - The King in Yellow

A cooperative top-down RPG set in the world of the King in Yellow / Cthulhu Mythos.
Built as a .NET 10 Native AOT compiled executable serving a Next.js React frontend.

## Overview

Carcosa is a 4-8 player co-op game where investigators must fight through waves of cultists
trying to resurrect an arcane beast in a 1920s coastal village. Players choose from three
synergistic classes and work together to survive 5 waves culminating in a boss fight against
the Herald of Hastur.

### Classes

| Class | Weapon | Special | Role |
|-------|--------|---------|------|
| **Gangster** | Tommy Gun (spray, 15 tile range, fast fire) | — | Area suppression |
| **Detective** | Magnum (single shot, 20 tile range, slow but 25 dmg) | — | Precision damage |
| **Surgeon** | Dagger (melee, 8 dmg) | Group Heal (15 HP to allies in 5 tiles) | Support/healer |

### Enemies

- **Cultist Acolyte** — Melee, low HP, swarms players
- **Cultist Chanter** — Ranged eldritch bolts, stays at distance
- **Cult Leader** — Tanky, AoE damage, buffs nearby cultists
- **Herald of Hastur** — Wave 5 boss, 500 HP, devastating area attacks

## Architecture

```
┌─────────────────────────────────────────┐
│         Carcosa.Server.exe (AOT)         │
├─────────────────────────────────────────┤
│  Kestrel HTTP + WebSocket Server         │
│  ├── Static file serving (wwwroot/)      │
│  └── /ws WebSocket endpoint              │
├─────────────────────────────────────────┤
│  Game Engine (20 tick/sec)               │
│  ├── Entity System                       │
│  ├── BSP Map Generator                   │
│  ├── Combat System                       │
│  ├── AI System (A* pathfinding)          │
│  ├── Wave System (5 waves + boss)        │
│  └── Session Manager                     │
├─────────────────────────────────────────┤
│  Next.js Frontend (static export)        │
│  ├── Canvas 2D Renderer                  │
│  ├── Client-side Prediction              │
│  ├── Entity Interpolation                │
│  └── Dungeon Crawler HUD                 │
└─────────────────────────────────────────┘
```

## Building

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 18+](https://nodejs.org/)
- Windows x64 (for the default publish target)

### Development Build

```bash
cd bbq/src/backend
dotnet build
```

This automatically:
1. Runs `npm ci` in the frontend directory
2. Builds the Next.js static export
3. Copies the output to `wwwroot/`
4. Compiles the .NET server

### Production Build (AOT)

```bash
cd bbq/src/backend
dotnet publish -c Release -r win-x64
```

The output is in `bin/Release/net10.0/win-x64/publish/`:
- `Carcosa.Server.exe` — The AOT-compiled native binary (~10 MB)
- `wwwroot/` — The frontend static files

### Running

```bash
# Default (port 5000)
./Carcosa.Server.exe

# Custom port
./Carcosa.Server.exe --port=8080

# Server-only mode (for dedicated hosting)
./Carcosa.Server.exe --headless

# Show help
./Carcosa.Server.exe --help
```

Then open `http://localhost:5000` in your browser.

## How to Play

### Hosting a Game

1. Run the exe
2. Open `http://localhost:5000` in your browser
3. Enter your name and connect — you become the host
4. Select a class
5. Click "Ready Up"
6. Click "Start Game" when all players are ready

### Joining a Game

1. Get the host's IP address and port
2. Open `http://<host-ip>:5000` in your browser
3. Enter your name and connect
4. Select a class and ready up

### Controls

| Key | Action |
|-----|--------|
| WASD / Arrows | Move |
| Left Click / Space | Primary Attack |
| E | Special Ability (Surgeon: Group Heal) |
| F | Revive downed ally (hold for 3 seconds) |
| Enter | Focus chat input |

## Technical Highlights

### .NET 10 AOT Best Practices

- `WebApplication.CreateSlimBuilder` for minimal startup
- Source-generated `JsonSerializerContext` (no runtime reflection)
- Raw WebSockets (no SignalR — fully AOT compatible)
- `InvariantGlobalization` for smaller binary
- `TrimMode=full` with AOT/Trim analyzers enabled
- No MVC, no Razor — minimal APIs only

### React / Next.js Best Practices

- Static export (`output: 'export'`) for embedding
- TypeScript throughout
- Custom hooks (`useWebSocket`, `useGameInput`)
- Component composition (GameCanvas, GameHUD, Lobby)
- Client-side prediction with server reconciliation
- `requestAnimationFrame` render loop (60fps) independent of server tick rate (20Hz)
- Entity interpolation for smooth movement between server updates

### Multiplayer Architecture

- Server-authoritative game state
- Peer-hosted model (one player's exe is the server)
- WebSocket binary framing with JSON messages
- Delta state broadcasting (only changed entities)
- Input sequence numbers for client reconciliation
- 20 tick/sec server → 60fps client interpolation

## Project Structure

```
bbq/
├── .vscode/              # VS Code tasks & launch configs
├── src/
│   ├── backend/          # .NET 10 AOT server
│   │   ├── Game/         # Game logic (loop, entities, combat, AI, maps)
│   │   ├── Network/      # WebSocket, messages, JSON contexts
│   │   └── Program.cs    # Entry point & HTTP pipeline
│   └── frontend/         # Next.js React client
│       ├── app/          # Pages & layout
│       ├── components/   # GameCanvas, GameHUD, Lobby
│       ├── hooks/        # useWebSocket, useGameInput
│       └── lib/          # Engine (camera, renderer, input, prediction, interpolation)
└── README.md
```

## License

This project is for educational/demonstration purposes.
