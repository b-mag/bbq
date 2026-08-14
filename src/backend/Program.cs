// =============================================================================
// Program.cs — CARCOSA Game Server Entry Point
// =============================================================================
//
// WHY THIS ARCHITECTURE:
// This file bootstraps the entire game server as a single self-contained process.
// We use ASP.NET Core's minimal API pattern (no controllers, no MVC) because:
//   1. Native AOT requires avoiding runtime reflection — minimal APIs use source generators
//   2. We only need a WebSocket endpoint and a few REST endpoints (health, map info)
//   3. The server's primary job is running the game loop, not serving HTTP requests
//
// WHY CreateSlimBuilder:
// `CreateSlimBuilder` is specifically designed for AOT scenarios. It excludes
// features we don't need (Razor, MVC, etc.) resulting in a smaller binary and
// faster startup. The regular `CreateBuilder` pulls in components that require
// runtime code generation which breaks under AOT compilation.
//
// WHY NO SignalR:
// SignalR uses runtime proxy generation and dynamic dispatch internally which is
// incompatible with Native AOT's ahead-of-time compilation model. Raw WebSockets
// give us full control, lower overhead, and work perfectly under AOT. The tradeoff
// is we must handle reconnection, message framing, and groups ourselves — but for
// a real-time game this level of control is actually preferable.
//
// DEPLOYMENT MODEL:
// The server is peer-hosted: one player runs the exe, others connect to their IP.
// The exe serves both the game logic AND the static frontend files (Next.js export
// copied into wwwroot/ during build). This means a single file distribution.
// =============================================================================

using System.Text.Json.Serialization;
using Carcosa.Server.Game;
using Carcosa.Server.Gameplay;
using Carcosa.Server.Network;
using Carcosa.Server.Cryptol;
using Carcosa.Server.P2P;

// --- CLI Arguments ---
// Parse command-line options for port and headless mode.
// These are simple string checks rather than a CLI parsing library to keep
// the dependency graph minimal for AOT (fewer assemblies to trim/compile).
var port = 5000;
var headless = false;
var spawnBots = 0;
var peerExchangeSettings = PeerExchangeSettings.FromArgs(args);

// Show help
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("CARCOSA Co-op RPG Server");
    Console.WriteLine();
    Console.WriteLine("Usage: Carcosa.Server [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --port=<port>   Set the listening port (default: 5000)");
    Console.WriteLine("  --headless      Run in server-only mode (no browser needed)");
    Console.WriteLine("  --spawn-bots=N  Spawn N bot players on startup");
    Console.WriteLine("  --help, -h      Show this help message");
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);

// WHY: Set content root to exe directory so that published deployments find wwwroot/
// correctly regardless of the current working directory when launched.
builder.Environment.ContentRootPath = AppContext.BaseDirectory;
builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

// --- Load configuration from appsettings.json ---
// CLI args override config file values (standard .NET config precedence).
var config = builder.Configuration;
var carcosaConfig = config.GetSection("Carcosa");
port = carcosaConfig.GetValue("Port", port);
headless = carcosaConfig.GetValue("Headless", headless);
spawnBots = carcosaConfig.GetValue("SpawnBots", spawnBots);
var serverName = carcosaConfig.GetValue("ServerName", "Carcosa Server") ?? "Carcosa Server";
var maxPlayers = carcosaConfig.GetValue("MaxPlayers", 8);

// Matchmaking configuration
var matchmakingConfig = new MatchmakingConfig();
var matchmakingSection = carcosaConfig.GetSection("Matchmaking");
matchmakingConfig.Url = matchmakingSection.GetValue("Url", matchmakingConfig.Url) ?? matchmakingConfig.Url;
matchmakingConfig.Enabled = matchmakingSection.GetValue("Enabled", matchmakingConfig.Enabled);
matchmakingConfig.HeartbeatIntervalSeconds = matchmakingSection.GetValue("HeartbeatIntervalSeconds", 10);

// CLI overrides for matchmaking URL
var matchmakingUrlArg = args.FirstOrDefault(a => a.StartsWith("--matchmaking-url="));
if (matchmakingUrlArg != null) matchmakingConfig.Url = matchmakingUrlArg[18..];

// CLI overrides for other settings
if (args.Contains("--headless")) headless = true;
var portArgOverride = args.FirstOrDefault(a => a.StartsWith("--port="));
if (portArgOverride != null && int.TryParse(portArgOverride.AsSpan(7), out var cliPort)) port = cliPort;
var botsArgOverride = args.FirstOrDefault(a => a.StartsWith("--spawn-bots="));
if (botsArgOverride != null && int.TryParse(botsArgOverride.AsSpan(13), out var cliBots)) spawnBots = cliBots;

// Dungeon instance mode: when launched with --seed and --scenario, the server
// generates a specific dungeon and auto-starts the game (no lobby needed).
// Used when the overworld server tells the party leader to host a dungeon.
int? dungeonSeed = null;
string? dungeonScenario = null;
var seedArg = args.FirstOrDefault(a => a.StartsWith("--seed="));
if (seedArg != null && int.TryParse(seedArg.AsSpan(7), out var parsedSeed)) dungeonSeed = parsedSeed;
var scenarioArg = args.FirstOrDefault(a => a.StartsWith("--scenario="));
if (scenarioArg != null) dungeonScenario = scenarioArg[11..];

// WHY: AOT requires all JSON serialization to go through source-generated contexts.
// This line registers our GameJsonContext (which handles WebSocket messages) plus
// AppJsonContext (which handles HTTP API responses) so the minimal API endpoints
// can serialize without reflection.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// WHY SINGLETONS: The game has exactly one game loop, one connection manager, and
// one session manager for the lifetime of the process. Singleton lifetime ensures
// all WebSocket handlers and API endpoints share the same instance.
builder.Services.AddSingleton(peerExchangeSettings);
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<CryptolStore>();
builder.Services.AddSingleton<NatTraversalService>();
builder.Services.AddSingleton(new MatchmakingClient(matchmakingConfig, serverName, port));

// P2P Mesh: Load or create our persistent peer identity, then register the mesh
var playerNameForPeer = args.FirstOrDefault(a => a.StartsWith("--name="))?.Substring(7) ?? serverName;
// Use port in identity filename to ensure separate identities when running multiple instances
var peerIdentity = PeerIdentityStore.LoadOrCreate(playerNameForPeer, port);
Console.WriteLine($"[P2P] Peer ID: {peerIdentity.PeerId}, Protocol: {PeerProtocol.ProtocolVersion}, " +
    $"Game: {PeerProtocol.GameVersionString}");
builder.Services.AddSingleton(peerIdentity);
builder.Services.AddSingleton(new PeerMesh(peerIdentity));
builder.Services.AddSingleton(sp => new OverworldSync(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>()));
builder.Services.AddSingleton(sp => new ShardHostManager(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>()));
builder.Services.AddSingleton(sp => new TaskAssignmentManager(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>()));
builder.Services.AddSingleton(sp => new MetricsCollector(
    sp.GetRequiredService<PeerIdentity>(),
    sp.GetRequiredService<PeerMesh>()));
builder.Services.AddSingleton<EnemySpawner>();
builder.Services.AddSingleton<LootDropManager>();
builder.Services.AddSingleton<PlayerInventory>();
builder.Services.AddSingleton(sp => new OverworldCombatSync(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>(),
    sp.GetRequiredService<ShardHostManager>(),
    sp.GetRequiredService<TaskAssignmentManager>(),
    sp.GetRequiredService<EnemySpawner>(),
    sp.GetRequiredService<OverworldSync>(),
    sp.GetRequiredService<LootDropManager>(),
    sp.GetRequiredService<PlayerInventory>(),
    sp.GetRequiredService<MetricsCollector>()));
builder.Services.AddSingleton(sp => new PeerExchange(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>(),
    sp.GetRequiredService<PeerExchangeSettings>()));
builder.Services.AddSingleton(sp => new PeerValidator(
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<PeerIdentity>()));
builder.Services.AddSingleton(sp => new WorldShard(
    sp.GetRequiredService<PeerIdentity>(),
    sp.GetRequiredService<PeerMesh>()));
builder.Services.AddSingleton(sp => new TrackerClient(
    matchmakingConfig.Url,
    sp.GetRequiredService<PeerIdentity>(),
    sp.GetRequiredService<PeerMesh>(),
    sp.GetRequiredService<WorldShard>()));
builder.Services.AddSingleton<GameLoop>(sp =>
{
    var cm = sp.GetRequiredService<ConnectionManager>();
    return new GameLoop(cm);
});
builder.Services.AddSingleton<SessionManager>(sp =>
{
    var cm = sp.GetRequiredService<ConnectionManager>();
    var gl = sp.GetRequiredService<GameLoop>();
    var cs = sp.GetRequiredService<CryptolStore>();
    return new SessionManager(cm, gl, cs);
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port);
});

var app = builder.Build();

// Get services
var connectionManager = app.Services.GetRequiredService<ConnectionManager>();
var gameLoop = app.Services.GetRequiredService<GameLoop>();
var sessionManager = app.Services.GetRequiredService<SessionManager>();

// WHY: The game loop needs to call SessionManager.EndGame() when victory/defeat occurs,
// but SessionManager is created after GameLoop. This circular reference is resolved by
// setting it after both are constructed.
gameLoop.Session = sessionManager;

// Dungeon Instance Mode: If --seed and --scenario are provided, pre-generate the map.
// When players connect, they skip the lobby and go directly into the dungeon.
if (dungeonSeed.HasValue && dungeonScenario != null)
{
    Console.WriteLine($"[Dungeon Instance] Seed: {dungeonSeed.Value}, Scenario: {dungeonScenario}");
    sessionManager.SelectedScenario = dungeonScenario switch
    {
        "temple" => MapScenario.PallidSanctum,
        _ => MapScenario.DrownedDock,
    };

    // Pre-generate the map from the seed
    var mapSeed = dungeonSeed.Value;
    gameLoop.State.Scenario = sessionManager.SelectedScenario;
    gameLoop.State.Map = sessionManager.SelectedScenario switch
    {
        MapScenario.PallidSanctum => MapGenerator.GenerateTemple(100, 100, mapSeed),
        _ => MapGenerator.Generate(80, 60, mapSeed),
    };
    Console.WriteLine($"[Dungeon Instance] Map generated: {gameLoop.State.Map.Width}x{gameLoop.State.Map.Height}");

    // Override OnPlayerConnected to auto-start when first player joins
    connectionManager.OnPlayerConnected += (playerId, playerName) =>
    {
        if (sessionManager.State == SessionState.Lobby && gameLoop.State.Map != null)
        {
            // Auto-transition to playing state
            sessionManager.AddPlayer(playerId, playerName);

            // Select a default class and mark ready
            sessionManager.SelectClass(playerId, "detective");
            sessionManager.SetReady(playerId, true);

            // Start the game
            sessionManager.TryStartGame(playerId);
        }
    };
}

// Start the game loop on its dedicated background thread
gameLoop.Start();

// Start P2P overworld state sync (broadcasts local player to mesh peers)
var overworldSync = app.Services.GetRequiredService<OverworldSync>();
var peerValidator = app.Services.GetRequiredService<PeerValidator>();
overworldSync.SetValidator(peerValidator);

// Initialize local player at the overworld spawn point (100.5, 180.5 — fishing village)
// These coordinates match the OverworldGenerator's SpawnPoint { X=100, Y=180 }
overworldSync.UpdateLocalPosition(100.5f, 180.5f, 0, 0);

// Ensure WorldId and PublicAddress are set before tracker registration
var worldShard = app.Services.GetRequiredService<WorldShard>();
var natTraversalService = app.Services.GetRequiredService<NatTraversalService>();
peerIdentity.PublicAddress = await natTraversalService.DiscoverAndApplyAsync(peerIdentity, port);

Console.WriteLine($"[P2P:Init] Local player initialized at spawn point (100.5, 180.5)");
Console.WriteLine($"[P2P:Init] Peer ID: {peerIdentity.PeerId}");
Console.WriteLine($"[P2P:Init] World: {peerIdentity.WorldId}");
Console.WriteLine($"[P2P:Init] Public Address: {peerIdentity.PublicAddress}");

overworldSync.Start();

// Start the overworld combat sync (enemy AI, projectile processing, P2P combat)
var combatSync = app.Services.GetRequiredService<OverworldCombatSync>();
combatSync.Start();

// Start Peer Exchange (periodic sharing of known peer lists for mesh discovery)
var peerExchange = app.Services.GetRequiredService<PeerExchange>();
peerExchange.Start();

// Attempt to bootstrap from cached peers (in case tracker is unavailable)
_ = Task.Run(async () =>
{
    await Task.Delay(2000); // Wait for server to be ready
    await peerExchange.ConnectFromCacheAsync();
});

// Start tracker client (optional peer discovery via matchmaking service)
var trackerClient = app.Services.GetRequiredService<TrackerClient>();
trackerClient.OnAdminMessage += (admin) =>
{
    overworldSync.AddAdminMessage(admin.MessageId, admin.Message, admin.Priority, admin.DurationSeconds, admin.Timestamp);
};
trackerClient.Start();

// Check matchmaking service connectivity — polls every 5 seconds.
// Starts heartbeat when online, stops when it goes offline. Fully dynamic.
var matchmakingClient = app.Services.GetRequiredService<MatchmakingClient>();
_ = Task.Run(async () =>
{
    // Wait for HTTP server to be ready
    await Task.Delay(2000);

    while (true)
    {
        var wasOnline = matchmakingClient.IsOnline;
        var isNowOnline = await matchmakingClient.CheckOnlineAsync();

        if (isNowOnline && !wasOnline)
        {
            // Just came online — start heartbeat
            matchmakingClient.StartHeartbeat(() => new SessionHeartbeatData
            {
                SessionId = sessionManager.SessionId,
                HostAddress = $"localhost:{port}", // In production, this would be the public IP
                PlayerCount = connectionManager.ConnectionCount,
                MaxPlayers = maxPlayers,
                State = sessionManager.State.ToString().ToLowerInvariant(),
                Scenario = sessionManager.SelectedScenario.ToString().ToLowerInvariant(),
                CurrentWave = gameLoop.State.CurrentWave,
            });
        }
        else if (!isNowOnline && wasOnline)
        {
            // Just went offline — stop heartbeat
            matchmakingClient.StopHeartbeat();
        }

        await Task.Delay(5000); // Poll every 5 seconds
    }
});

// WHY: Wire up message routing based on message type. This acts as the central
// message dispatcher. WebSocket messages arrive on HTTP thread pool threads and
// are routed to the appropriate subsystem. The game loop thread only reads from
// the InputQueue, avoiding contention.
connectionManager.OnMessageReceived += async (senderId, message) =>
{
    switch (message.Type)
    {
        case MessageTypes.Chat:
            // Chat messages are simply relayed to all other players.
            // No server-side processing needed — the message content is a pre-defined string.
            await connectionManager.BroadcastExceptAsync(senderId, message);
            break;

        case MessageTypes.Ping:
            // Respond with pong for latency measurement.
            // Client sends timestamp → server echoes it back with server timestamp.
            var pong = new GameMessage
            {
                Type = MessageTypes.Pong,
                Pong = new PongPayload
                {
                    ClientTimestamp = message.Ping?.ClientTimestamp ?? 0,
                    ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            };
            await connectionManager.SendToAsync(senderId, pong);
            break;

        case MessageTypes.PlayerInput:
            // WHY QUEUE: Player inputs are queued rather than processed immediately
            // because they arrive on arbitrary thread pool threads but must be processed
            // deterministically on the game loop thread (one tick at a time, in order).
            if (message.PlayerInput != null)
            {
                gameLoop.InputQueue.Enqueue(senderId, message.PlayerInput);
            }
            break;

        case MessageTypes.SessionAction:
            // Session actions (class select, ready, start game) modify lobby state.
            sessionManager.HandleMessage(senderId, message);
            break;
    }
};

// WHY: Player connect/disconnect events go through SessionManager so it can
// track lobby state, assign host, and clean up on disconnect.
connectionManager.OnPlayerConnected += (playerId, playerName) =>
{
    sessionManager.AddPlayer(playerId, playerName);
};

connectionManager.OnPlayerDisconnected += (playerId) =>
{
    sessionManager.RemovePlayer(playerId);
};

// Enable WebSockets with a 30-second keep-alive to detect dead connections
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// --- P2P Peer-to-Peer WebSocket Endpoint ---
// Other Carcosa.Server instances connect here to form the mesh.
// This is separate from /ws (player client connections).
app.Map("/ws/peer", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connection required for peer mesh");
        return;
    }

    var peerMesh = context.RequestServices.GetRequiredService<PeerMesh>();
    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    await peerMesh.HandleInboundPeerAsync(webSocket, context.RequestAborted);
});

// WHY: Single WebSocket endpoint at /ws. Each connection is a player session.
// The player name is passed as a query parameter for simplicity (no auth yet).
// The endpoint handles the full lifecycle: accept → assign ID → notify others → receive loop → cleanup.
app.Map("/ws", async (HttpContext context, ConnectionManager connectionManager, SessionManager sessionManager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connections only");
        return;
    }

    var playerName = context.Request.Query["name"].FirstOrDefault() ?? "Unknown";
    // Generate a short unique ID for this player session.
    // Using first 8 chars of a GUID gives sufficient uniqueness for a single server.
    var playerId = Guid.NewGuid().ToString("N")[..8];

    if (connectionManager.ConnectionCount >= connectionManager.MaxConnections)
    {
        context.Response.StatusCode = 503;
        await context.Response.WriteAsync("Server full");
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();

    if (!connectionManager.TryAddConnection(playerId, playerName, webSocket))
    {
        await webSocket.CloseAsync(
            System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
            "Server full",
            CancellationToken.None);
        return;
    }

    Console.WriteLine($"[WS] Player connected: {playerName} ({playerId})");

    // Send the player their assigned ID so the client knows which entity is "self"
    await connectionManager.SendToAsync(playerId, new GameMessage
    {
        Type = MessageTypes.PlayerJoined,
        PlayerJoined = new PlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName
        }
    });

    // Notify all other connected players about the new arrival
    await connectionManager.BroadcastExceptAsync(playerId, new GameMessage
    {
        Type = MessageTypes.PlayerJoined,
        PlayerJoined = new PlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName
        }
    });

    // Block here receiving messages until the player disconnects.
    // This is the standard ASP.NET Core WebSocket pattern — the request stays
    // alive for the duration of the connection.
    await connectionManager.HandleConnectionAsync(playerId, context.RequestAborted);

    Console.WriteLine($"[WS] Player disconnected: {playerName} ({playerId})");

    // Notify remaining players about the disconnect
    await connectionManager.BroadcastAsync(new GameMessage
    {
        Type = MessageTypes.PlayerLeft,
        PlayerLeft = new PlayerLeftPayload
        {
            PlayerId = playerId,
            Reason = "disconnected"
        }
    });
});

// WHY: Health check endpoint for monitoring and for the matchmaking service to
// verify a game server is alive and accepting connections.
app.MapGet("/api/health", (ConnectionManager cm, GameLoop gl, SessionManager sm) => new HealthResponse(
    "Carcosa Server",
    "1.0.0",
    DateTime.UtcNow,
    cm.ConnectionCount,
    gl.State.Tick,
    gl.IsRunning,
    sm.State.ToString()));

// WHY: Map info endpoint for debugging and for clients that want map metadata
// before connecting to the WebSocket (e.g., a server browser showing map size).
app.MapGet("/api/map", (GameLoop gl) =>
{
    if (gl.State.Map == null)
        return Results.NotFound();

    return Results.Ok(new MapInfoResponse(
        gl.State.Map.Width,
        gl.State.Map.Height,
        gl.State.Map.Seed,
        gl.State.Map.Rooms.Length,
        gl.State.Map.SpawnPoints.Length));
});

// Matchmaking status — tells the frontend if online mode is available
app.MapGet("/api/matchmaking-status", (MatchmakingClient mm) => new MatchmakingStatusResponse(
    mm.IsOnline,
    mm.LastContact));

// Available sessions — queries the matchmaking service for joinable games
app.MapGet("/api/available-sessions", async (MatchmakingClient mm) =>
{
    var sessions = await mm.GetAvailableSessionsAsync();
    return Results.Ok(sessions);
});

// --- P2P Overworld State API (queried by local frontend) ---

// Overworld map data is bundled with the peer client and versioned by major game version.
// This keeps the mesh self-supporting when the tracker is offline.
app.MapGet("/api/p2p/map", () =>
{
    try
    {
        var json = StaticOverworldAsset.LoadJson(PeerProtocol.GameVersionMajor);
        return Results.Content(json, "application/json");
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound();
    }
});

// Get all visible players in the overworld (local + remote peers)
var p2pPlayersCallCount = 0;
app.MapGet("/api/p2p/players", (OverworldSync sync) =>
{
    var count = Interlocked.Increment(ref p2pPlayersCallCount);
    if (count <= 3 || count % 100 == 0) // Log first 3 calls then every 100th
    {
        var players = sync.GetAllVisiblePlayers();
        Console.WriteLine($"[P2P:API] GET /api/p2p/players (call #{count}) → {players.Count} player(s)");
        foreach (var p in players)
            Console.WriteLine($"[P2P:API]   {p.DisplayName} ({p.PeerId}) at ({p.X:F1}, {p.Y:F1})");
    }

    var result = sync.GetAllVisiblePlayers().Select(p => new P2PPlayerResponse(
        p.PeerId, p.DisplayName, p.X, p.Y, p.VelocityX, p.VelocityY,
        p.Status, p.PartyId ?? "", p.IsPartyLeader)).ToArray();
    return Results.Ok(result);
});

// Update local player position (called by frontend at 20Hz for mesh broadcast)
var p2pPositionCallCount = 0;
app.MapPost("/api/p2p/position", (P2PPositionUpdate update, OverworldSync sync, OverworldCombatSync combat) =>
{
    var count = Interlocked.Increment(ref p2pPositionCallCount);
    if (count <= 3 || count % 200 == 0)
    {
        Console.WriteLine($"[P2P:API] POST /api/p2p/position (call #{count}) → ({update.X:F1}, {update.Y:F1})");
    }
    sync.UpdateLocalPosition(update.X, update.Y, update.VelocityX, update.VelocityY);
    combat.UpdateLocalPlayerPosition(update.X, update.Y);
    return Results.Ok();
});

// Set the local player's display name (called by frontend after entering name)
app.MapPost("/api/p2p/name", (P2PNameRequest request, PeerIdentity identity) =>
{
    if (!string.IsNullOrWhiteSpace(request.Name))
    {
        identity.DisplayName = request.Name.Trim();
        Console.WriteLine($"[P2P:API] Player name set to: {identity.DisplayName}");
    }
    return Results.Ok();
});

// --- DEBUG: Log what /api/p2p/players is returning ---
app.MapGet("/api/p2p/debug", (OverworldSync sync, PeerIdentity identity, PeerMesh mesh) =>
{
    var allPlayers = sync.GetAllVisiblePlayers();
    Console.WriteLine($"[P2P:Debug] /api/p2p/debug called");
    Console.WriteLine($"[P2P:Debug]   Local peer ID: {identity.PeerId}");
    Console.WriteLine($"[P2P:Debug]   Local address: {identity.PublicAddress}");
    Console.WriteLine($"[P2P:Debug]   World: {identity.WorldId}");
    Console.WriteLine($"[P2P:Debug]   Mesh peers: {mesh.PeerCount}");
    Console.WriteLine($"[P2P:Debug]   Visible players: {allPlayers.Count}");
    foreach (var p in allPlayers)
    {
        Console.WriteLine($"[P2P:Debug]     - {p.DisplayName} ({p.PeerId}) at ({p.X:F1}, {p.Y:F1}) status={p.Status}");
    }
    return Results.Ok(new P2PMessageResponse(
        $"Players: {allPlayers.Count}, Peers: {mesh.PeerCount}, Local: ({identity.PeerId})",
        identity.PublicAddress));
});

// Get mesh status (peer count, our identity, connectivity)
app.MapGet("/api/p2p/status", (PeerMesh mesh, PeerIdentity identity) =>
{
    var peers = mesh.Connections.Select(c => new P2PPeerInfo(
        c.RemotePeerId, c.RemoteDisplayName, c.LatencyMs)).ToArray();
    var response = new P2PStatusResponse(
        identity.PeerId, identity.DisplayName, identity.WorldId,
        mesh.PeerCount, PeerProtocol.GameVersionString, PeerProtocol.ProtocolVersion, peers);
    return Results.Ok(response);
});

// --- Glyph System API ---

// Generate a Glyph code for this peer (so the player can share it)
app.MapGet("/api/p2p/glyph", (PeerIdentity identity) =>
{
    var glyph = GlyphCodec.GenerateForPeer(identity);
    return Results.Ok(new P2PGlyphResponse(glyph, identity.WorldId, identity.PublicAddress));
});

// Connect to a peer using a Glyph code (manual discovery)
app.MapPost("/api/p2p/glyph/connect", async (GlyphConnectRequest request, PeerMesh mesh) =>
{
    var decoded = GlyphCodec.DecodeToAddress(request.Glyph);
    if (decoded == null)
        return Results.BadRequest(new P2PErrorResponse("Invalid Glyph code"));

    var (address, worldIndex) = decoded.Value;
    Console.WriteLine($"[P2P:Glyph] Connecting to {address} (world index: {worldIndex}) from Glyph: {request.Glyph}");

    var success = await mesh.ConnectToPeerAsync(address);
    return success
        ? Results.Ok(new P2PMessageResponse($"Connected to peer at {address}", address))
        : Results.BadRequest(new P2PErrorResponse($"Failed to connect to {address}"));
});

// Get recent admin broadcast messages (for frontend display)
app.MapGet("/api/p2p/admin-messages", (OverworldSync sync) =>
{
    var messages = sync.GetAdminMessages().Select(m => new P2PAdminMessageResponse(
        m.MessageId, m.Message, m.Priority, m.DurationSeconds, m.Timestamp)).ToArray();
    return Results.Ok(messages);
});

// --- P2P Chat API ---

// Send a chat message (broadcast to all mesh peers)
app.MapPost("/api/p2p/chat", async (P2PChatRequest request, OverworldSync sync) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
        return Results.BadRequest(new P2PErrorResponse("Message text is required"));

    await sync.SendChatAsync(request.Channel ?? "global", request.Text.Trim());
    return Results.Ok();
});

// Get recent chat messages (frontend polls this)
app.MapGet("/api/p2p/chat/messages", (OverworldSync sync, HttpContext ctx) =>
{
    var sinceStr = ctx.Request.Query["since"].FirstOrDefault();
    var since = long.TryParse(sinceStr, out var s) ? s : 0;
    var messages = sync.GetRecentChat(since).Select(m => new P2PChatMessageResponse(
        m.MessageId, m.Channel, m.SenderId, m.SenderName, m.Text, m.Timestamp)).ToArray();
    return Results.Ok(messages);
});

// Get world shard info (current shard, capacity, player count)
app.MapGet("/api/p2p/shard", (WorldShard shard) =>
{
    var info = new P2PShardResponse(
        shard.CurrentShardId, shard.CurrentShardIndex,
        shard.PlayerCount, PeerProtocol.MaxPeersPerWorld, shard.IsAtCapacity);
    return Results.Ok(info);
});

// Switch to a different world shard
app.MapPost("/api/p2p/shard/switch", async (ShardSwitchRequest request, WorldShard shard) =>
{
    if (string.IsNullOrWhiteSpace(request.ShardId))
        return Results.BadRequest(new P2PErrorResponse("shardId is required"));

    await shard.SwitchShardAsync(request.ShardId);
    return Results.Ok(new P2PMessageResponse($"Switched to shard {request.ShardId}", request.ShardId));
});

// =============================================================================
// GAMEPLAY API ENDPOINTS (Phase B — Combat, Enemies, Player Stats)
// =============================================================================
// These endpoints support the overworld combat system. The frontend polls these
// at 10Hz for responsive combat UI (stamina bar, enemy positions, cooldowns).

// Get local player stats (HP, stamina, abilities, cooldowns) — polled at 10Hz by frontend
app.MapGet("/api/gameplay/player-stats", (OverworldCombatSync combat) =>
{
    var p = combat.LocalPlayer;
    return Results.Ok(new PlayerStatsResponse(
        Hp: p.Health, MaxHp: p.MaxHealth,
        Stamina: p.Stamina, MaxStamina: p.MaxStamina,
        IsStaminaDepleted: p.IsStaminaDepleted,
        Level: p.Level, Xp: p.XP,
        PrimaryAbility: p.PrimaryAbility, SecondaryAbility: p.SecondaryAbility,
        PrimaryCooldown: p.PrimaryFireCooldown, SecondaryCooldown: p.SecondaryAbilityCooldown,
        ShieldHp: p.ShieldHP, IsShardHost: combat.IsHost));
});

// Get all enemies (for frontend rendering) — polled at 10Hz
app.MapGet("/api/gameplay/enemies", (OverworldCombatSync combat) =>
{
    var enemies = combat.GetEnemiesForRendering();
    var entries = enemies.Select(e => new EnemyStateEntry(
        e.Id, e.SubType, e.X, e.Y, e.VelocityX, e.VelocityY,
        e.Health, e.MaxHealth, e.IsAlive, e.TaggedBy)).ToArray();
    return Results.Ok(new EnemyListResponse(entries));
});

// Get active projectiles (for frontend rendering) — polled at 10Hz
app.MapGet("/api/gameplay/projectiles", (OverworldCombatSync combat) =>
{
    var projectiles = combat.GetProjectilesForRendering();
    var entries = projectiles.Select(p => new ProjectileEntry(
        p.Id, p.SubType, p.X, p.Y, p.VelocityX, p.VelocityY)).ToArray();
    return Results.Ok(new ProjectileListResponse(entries));
});

// Execute a combat action (ability use) — called by frontend on click
app.MapPost("/api/gameplay/combat-action", async (CombatActionRequest request, OverworldCombatSync combat) =>
{
    if (string.IsNullOrWhiteSpace(request.AbilitySlot))
        return Results.BadRequest(new CombatActionResponse(false, "abilitySlot is required"));

    var success = await combat.ProcessLocalCombatActionAsync(request.AbilitySlot, request.AimAngle);
    return Results.Ok(new CombatActionResponse(success, success ? null : "Ability failed (cooldown or stamina)"));
});

// Get player inventory (equipment + backpack)
app.MapGet("/api/gameplay/inventory", (PlayerInventory inventory) =>
{
    var equipment = new InventorySlotEntry?[4];
    var slots = new[] { ItemSlot.Weapon, ItemSlot.Armor, ItemSlot.Trinket, ItemSlot.Boots };
    for (int i = 0; i < 4; i++)
    {
        var item = inventory.GetEquipped(slots[i]);
        if (item != null)
        {
            var def = Carcosa.Server.Gameplay.ItemRegistry.GetItem(item.ItemId);
            equipment[i] = new InventorySlotEntry(item.ItemId, item.Quantity, def?.Name, def?.Rarity.ToString(), def?.Slot.ToString());
        }
    }

    var backpack = new InventorySlotEntry?[PlayerInventory.BackpackSize];
    for (int i = 0; i < PlayerInventory.BackpackSize; i++)
    {
        var bp = inventory.Backpack[i];
        if (bp != null)
        {
            var def = Carcosa.Server.Gameplay.ItemRegistry.GetItem(bp.ItemId);
            backpack[i] = new InventorySlotEntry(bp.ItemId, bp.Quantity, def?.Name, def?.Rarity.ToString(), def?.Slot.ToString());
        }
    }

    return Results.Ok(new InventoryResponse(equipment, backpack));
});

// Equip an item from backpack slot
app.MapPost("/api/gameplay/equip", (EquipRequest request, PlayerInventory inventory, OverworldCombatSync combat) =>
{
    var success = inventory.EquipFromBackpack(request.BackpackSlot, combat.LocalPlayer);
    return Results.Ok(new CombatActionResponse(success, success ? null : "Cannot equip item"));
});

// Pick up loot from ground
app.MapPost("/api/gameplay/pickup-loot", async (PickupLootRequest request, OverworldCombatSync combat, PlayerInventory inventory, PeerIdentity identity) =>
{
    var drop = await combat.TryPickUpLootAsync(request.DropId, identity.PeerId);
    if (drop == null)
        return Results.Ok(new PickupLootResponse(false, null, "Drop not found or not eligible"));

    if (!inventory.AddItem(drop.ItemId, drop.Quantity))
        return Results.Ok(new PickupLootResponse(false, null, "Inventory full"));

    return Results.Ok(new PickupLootResponse(true, drop.ItemId, null));
});

// Get visible loot drops for this player
app.MapGet("/api/gameplay/loot-drops", (LootDropManager lootManager, PeerIdentity identity, OverworldCombatSync combat) =>
{
    var drops = lootManager.GetDropsForPeer(identity.PeerId, combat.CurrentServerTick);
    var entries = drops.Select(d =>
    {
        var def = Carcosa.Server.Gameplay.ItemRegistry.GetItem(d.ItemId);
        return new LootDropEntry(d.DropId, d.ItemId, def?.Name ?? "Unknown", d.Rarity.ToString(), d.Quantity, d.X, d.Y);
    }).ToArray();
    return Results.Ok(new LootDropsResponse(entries));
});

// Swap abilities at a Meditation Altar
app.MapPost("/api/gameplay/swap-abilities", (SwapAbilitiesRequest request, OverworldCombatSync combat) =>
{
    if (string.IsNullOrWhiteSpace(request.Primary) || string.IsNullOrWhiteSpace(request.Secondary))
        return Results.BadRequest(new CombatActionResponse(false, "primary and secondary required"));

    combat.SetAbilities(request.Primary, request.Secondary);
    return Results.Ok(new CombatActionResponse(true, null));
});

// WHY: Static file serving for the embedded Next.js frontend.
// The frontend is built as a static export and copied into wwwroot/ during the
// .NET build process. This makes the entire game a single distributable unit.
app.UseDefaultFiles();
app.UseStaticFiles();

// WHY: SPA fallback — any non-API, non-file request returns index.html so that
// client-side routing works (though currently the app is single-page).
app.MapFallback(async context =>
{
    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    }
    else
    {
        context.Response.StatusCode = 404;
    }
});

// WHY: Graceful shutdown ensures the game loop thread terminates cleanly
// rather than being abruptly killed, which could leave resources in a bad state.
app.Lifetime.ApplicationStopping.Register(() =>
{
    gameLoop.Dispose();
});

Console.WriteLine("===========================================");
Console.WriteLine("  CARCOSA");
Console.WriteLine($"  Server running on http://0.0.0.0:{port}");
Console.WriteLine($"  WebSocket endpoint: ws://0.0.0.0:{port}/ws");
Console.WriteLine($"  Game loop: {GameLoop.TickRate} ticks/sec");
if (headless) Console.WriteLine("  Mode: Headless (server only)");
else Console.WriteLine($"  Open http://localhost:{port} in your browser");
if (spawnBots > 0) Console.WriteLine($"  Spawning {spawnBots} bot(s)...");
Console.WriteLine("===========================================");

// Spawn internal bots if requested (connects to self via WebSocket)
if (spawnBots > 0)
{
    _ = Task.Run(async () =>
    {
        // Wait for server to be ready
        await Task.Delay(2000);
        for (int i = 0; i < spawnBots; i++)
        {
            var botName = $"Bot_{i + 1}";
            _ = Task.Run(() => RunInternalBot($"ws://localhost:{port}", botName, i));
        }
    });
}

// Launch mode: headless (console) or windowed (native WebView2 window)
if (headless)
{
    // Headless: just run the web server as a console process
    app.Run();
}
else
{
    // Windowed: start web server, then open Edge in "app mode" (no URL bar, no tabs)
    // This looks like a native desktop app to the user.
    _ = Task.Run(() =>
    {
        // Wait for server to be ready, then launch the window
        using var httpClient = new HttpClient();
        for (int attempt = 0; attempt < 30; attempt++)
        {
            Thread.Sleep(500);
            try
            {
                var response = httpClient.GetAsync($"http://localhost:{port}/api/health").Result;
                if (response.IsSuccessStatusCode) break;
            }
            catch { }
        }

        // Launch Edge in app mode — creates a standalone window with no browser chrome
        var edgePath = FindEdgePath();
        if (edgePath != null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = $"--app=http://localhost:{port} --window-size=1280,800 --disable-extensions",
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
            Console.WriteLine("[Window] Launched game in Edge app mode");
        }
        else
        {
            // Fallback: open in default browser
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://localhost:{port}",
                UseShellExecute = true,
            });
            Console.WriteLine("[Window] Launched game in default browser");
        }
    });

    app.Run();
}

// Find Microsoft Edge executable path
static string? FindEdgePath()
{
    var paths = new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
    };
    return paths.FirstOrDefault(File.Exists);
}

// --- Internal bot implementation (simplified version of the standalone bot client) ---
static async Task RunInternalBot(string serverUrl, string name, int index)
{
    var rng = new Random(index * 7919);
    var classes = new[] { "gangster", "detective", "surgeon" };
    var selectedClass = classes[rng.Next(classes.Length)];

    try
    {
        using var ws = new System.Net.WebSockets.ClientWebSocket();
        await ws.ConnectAsync(new Uri($"{serverUrl}/ws?name={Uri.EscapeDataString(name)}"), CancellationToken.None);
        Console.WriteLine($"[Bot] {name} connected as {selectedClass}");

        string? myPlayerId = null;
        var inGame = false;
        float myX = 0, myY = 0;
        int myHealth = 100, myMaxHealth = 100, myMedKits = 0;
        var enemyPositions = new List<(float X, float Y)>();

        // Receive loop
        _ = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            while (ws.State == System.Net.WebSockets.WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) break;
                    var json = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);

                    // Minimal parsing for bot behavior
                    if (json.Contains("\"player_joined\"") && myPlayerId == null)
                    {
                        var idStart = json.IndexOf("\"playerId\":\"") + 12;
                        if (idStart > 12)
                        {
                            var idEnd = json.IndexOf('"', idStart);
                            myPlayerId = json[idStart..idEnd];
                        }
                    }
                    else if (json.Contains("\"map_data\""))
                    {
                        inGame = true;
                    }
                    else if (json.Contains("\"game_over\"") || json.Contains("\"victory\""))
                    {
                        inGame = false;
                    }
                }
                catch { break; }
            }
        });

        // Lobby actions
        await Task.Delay(1500);
        await SendBotMsg(ws, $"{{\"type\":\"session_action\",\"sessionAction\":{{\"action\":\"select_class\",\"value\":\"{selectedClass}\"}}}}");
        await Task.Delay(1000);
        await SendBotMsg(ws, "{\"type\":\"session_action\",\"sessionAction\":{\"action\":\"set_ready\",\"value\":\"true\"}}");

        // Game loop
        var seq = 0;
        var patrolAngle = rng.NextSingle() * MathF.PI * 2;
        while (ws.State == System.Net.WebSockets.WebSocketState.Open)
        {
            await Task.Delay(50);
            if (!inGame) continue;

            seq++;
            // Simple random movement + occasional firing
            if (rng.Next(20) == 0) patrolAngle = rng.NextSingle() * MathF.PI * 2;
            var moveX = MathF.Cos(patrolAngle) * 0.6f;
            var moveY = MathF.Sin(patrolAngle) * 0.6f;
            var fire = rng.Next(5) == 0; // Fire 20% of the time

            var input = $"{{\"type\":\"player_input\",\"playerInput\":{{\"sequenceNumber\":{seq},\"moveX\":{moveX:F2},\"moveY\":{moveY:F2},\"primaryFire\":{(fire ? "true" : "false")},\"secondaryAbility\":false,\"interact\":false,\"useMedKit\":false,\"aimAngle\":{patrolAngle:F3},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}}}";
            await SendBotMsg(ws, input);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Bot] {name} error: {ex.Message}");
    }
}

static async Task SendBotMsg(System.Net.WebSockets.ClientWebSocket ws, string json)
{
    if (ws.State == System.Net.WebSockets.WebSocketState.Open)
    {
        await ws.SendAsync(System.Text.Encoding.UTF8.GetBytes(json),
            System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
    }
}

// =============================================================================
// AOT-compatible types for HTTP API responses
// =============================================================================
// WHY RECORDS: Records provide value equality and concise syntax for data-only types.
// WHY SEPARATE JsonSerializerContext: The HTTP API types are separate from the game
// message types because they're used by different middleware (minimal API vs WebSocket).
// Each context generates its own serialization code at compile time.
// =============================================================================

internal record HealthResponse(
    string Name,
    string Version,
    DateTime Timestamp,
    int ConnectedPlayers,
    int GameTick,
    bool GameLoopRunning,
    string SessionState);

internal record MapInfoResponse(
    int Width,
    int Height,
    int Seed,
    int RoomCount,
    int SpawnPointCount);

internal record MatchmakingStatusResponse(
    bool IsOnline,
    DateTime LastContact);

internal record GlyphConnectRequest(string Glyph);
internal record ShardSwitchRequest(string ShardId);

// --- P2P API Response Types (required for AOT serialization — no anonymous objects) ---
internal record P2PPlayerResponse(
    string Id, string Name, float X, float Y, float VelocityX, float VelocityY,
    string Status, string PartyId, bool IsPartyLeader);
internal record P2PPeerInfo(string Id, string Name, int Latency);
internal record P2PStatusResponse(
    string PeerId, string DisplayName, string WorldId, int PeerCount,
    string GameVersion, int ProtocolVersion, P2PPeerInfo[] ConnectedPeers);
internal record P2PGlyphResponse(string Glyph, string WorldId, string Address);
internal record P2PMessageResponse(string Message, string Address);
internal record P2PErrorResponse(string Error);
internal record P2PAdminStatusResponse(bool TrackerOnline);
internal record P2PAdminMessageResponse(string MessageId, string Message, string Priority, int DurationSeconds, long Timestamp);
internal record P2PChatRequest(string? Channel, string Text);
internal record P2PChatMessageResponse(string MessageId, string Channel, string SenderId, string SenderName, string Text, long Timestamp);
internal record P2PShardResponse(
    string ShardId, byte ShardIndex, int PlayerCount, int MaxPlayers, bool IsAtCapacity);
internal record P2PPositionUpdate(float X, float Y, float VelocityX, float VelocityY);
internal record P2PNameRequest(string Name);

// =============================================================================
// GAMEPLAY API TYPES (Phase B — Combat, Enemies, Player Stats)
// =============================================================================

/// <summary>Request body for POST /api/gameplay/combat-action.</summary>
internal record CombatActionRequest(string AbilitySlot, float AimAngle);

/// <summary>Response for GET /api/gameplay/player-stats.</summary>
internal record PlayerStatsResponse(
    int Hp, int MaxHp, float Stamina, float MaxStamina, bool IsStaminaDepleted,
    int Level, int Xp, string PrimaryAbility, string SecondaryAbility,
    int PrimaryCooldown, int SecondaryCooldown, int ShieldHp, bool IsShardHost);

/// <summary>Single enemy entry for GET /api/gameplay/enemies response.</summary>
internal record EnemyStateEntry(
    string Id, string SubType, float X, float Y, float VelocityX, float VelocityY,
    int Health, int MaxHealth, bool IsAlive, string? TaggedBy);

/// <summary>Response for GET /api/gameplay/enemies.</summary>
internal record EnemyListResponse(EnemyStateEntry[] Enemies);

/// <summary>Single projectile entry for rendering.</summary>
internal record ProjectileEntry(
    string Id, string SubType, float X, float Y, float VelocityX, float VelocityY);

/// <summary>Response for GET /api/gameplay/projectiles.</summary>
internal record ProjectileListResponse(ProjectileEntry[] Projectiles);

/// <summary>Result of a combat action.</summary>
internal record CombatActionResponse(bool Success, string? Message);

// --- Inventory/Loot API Types ---
internal record InventorySlotEntry(string? ItemId, int Quantity, string? ItemName, string? Rarity, string? Slot);
internal record InventoryResponse(InventorySlotEntry?[] Equipment, InventorySlotEntry?[] Backpack);
internal record EquipRequest(int BackpackSlot);
internal record PickupLootRequest(string DropId);
internal record PickupLootResponse(bool Success, string? ItemId, string? Message);
internal record LootDropEntry(string DropId, string ItemId, string ItemName, string Rarity, int Quantity, float X, float Y);
internal record LootDropsResponse(LootDropEntry[] Drops);
internal record SwapAbilitiesRequest(string Primary, string Secondary);

/// <summary>
/// Source-generated JSON context for HTTP API response types.
/// AOT REQUIREMENT: Without this, the minimal API serializer would need runtime
/// reflection to discover properties — which doesn't exist in AOT builds.
/// Each [JsonSerializable] attribute causes the source generator to emit
/// optimized serialization code at compile time.
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(MapInfoResponse))]
[JsonSerializable(typeof(MatchmakingStatusResponse))]
[JsonSerializable(typeof(GlyphConnectRequest))]
[JsonSerializable(typeof(ShardSwitchRequest))]
[JsonSerializable(typeof(P2PPlayerResponse))]
[JsonSerializable(typeof(P2PPlayerResponse[]))]
[JsonSerializable(typeof(P2PPeerInfo))]
[JsonSerializable(typeof(P2PPeerInfo[]))]
[JsonSerializable(typeof(P2PStatusResponse))]
[JsonSerializable(typeof(P2PGlyphResponse))]
[JsonSerializable(typeof(P2PMessageResponse))]
[JsonSerializable(typeof(P2PErrorResponse))]
[JsonSerializable(typeof(P2PAdminStatusResponse))]
[JsonSerializable(typeof(P2PAdminMessageResponse))]
[JsonSerializable(typeof(P2PAdminMessageResponse[]))]
[JsonSerializable(typeof(P2PChatRequest))]
[JsonSerializable(typeof(P2PChatMessageResponse))]
[JsonSerializable(typeof(P2PChatMessageResponse[]))]
[JsonSerializable(typeof(P2PShardResponse))]
[JsonSerializable(typeof(P2PPositionUpdate))]
[JsonSerializable(typeof(P2PNameRequest))]
[JsonSerializable(typeof(CombatActionRequest))]
[JsonSerializable(typeof(PlayerStatsResponse))]
[JsonSerializable(typeof(EnemyStateEntry))]
[JsonSerializable(typeof(EnemyStateEntry[]))]
[JsonSerializable(typeof(EnemyListResponse))]
[JsonSerializable(typeof(ProjectileEntry))]
[JsonSerializable(typeof(ProjectileEntry[]))]
[JsonSerializable(typeof(ProjectileListResponse))]
[JsonSerializable(typeof(CombatActionResponse))]
[JsonSerializable(typeof(InventorySlotEntry))]
[JsonSerializable(typeof(InventorySlotEntry[]))]
[JsonSerializable(typeof(InventoryResponse))]
[JsonSerializable(typeof(EquipRequest))]
[JsonSerializable(typeof(PickupLootRequest))]
[JsonSerializable(typeof(PickupLootResponse))]
[JsonSerializable(typeof(LootDropEntry))]
[JsonSerializable(typeof(LootDropEntry[]))]
[JsonSerializable(typeof(LootDropsResponse))]
[JsonSerializable(typeof(SwapAbilitiesRequest))]
[JsonSerializable(typeof(List<AvailableSession>))]
[JsonSerializable(typeof(AvailableSession))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
