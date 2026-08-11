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
using Carcosa.Server.Network;
using Carcosa.Server.Cryptol;

// --- CLI Arguments ---
// Parse command-line options for port and headless mode.
// These are simple string checks rather than a CLI parsing library to keep
// the dependency graph minimal for AOT (fewer assemblies to trim/compile).
var port = 5000;
var headless = args.Contains("--headless");
var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
if (portArg != null && int.TryParse(portArg.AsSpan(7), out var customPort))
{
    port = customPort;
}
// Bot spawning: --spawn-bots=N spawns N internal bot clients after startup
var spawnBots = 0;
var botsArg = args.FirstOrDefault(a => a.StartsWith("--spawn-bots="));
if (botsArg != null && int.TryParse(botsArg.AsSpan(13), out var botCount))
{
    spawnBots = botCount;
}

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
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<CryptolStore>();
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

// Start the game loop on its dedicated background thread
gameLoop.Start();

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

/// <summary>
/// Source-generated JSON context for HTTP API response types.
/// AOT REQUIREMENT: Without this, the minimal API serializer would need runtime
/// reflection to discover properties — which doesn't exist in AOT builds.
/// Each [JsonSerializable] attribute causes the source generator to emit
/// optimized serialization code at compile time.
/// </summary>
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(MapInfoResponse))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
