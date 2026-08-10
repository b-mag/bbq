using System.Text.Json.Serialization;
using Carcosa.Server.Game;
using Carcosa.Server.Network;

// --- CLI Arguments ---
var port = 5000;
var headless = args.Contains("--headless");
var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
if (portArg != null && int.TryParse(portArg.AsSpan(7), out var customPort))
{
    port = customPort;
}

// Show help
if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("CARCOSA - The King in Yellow Co-op RPG Server");
    Console.WriteLine();
    Console.WriteLine("Usage: Carcosa.Server [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --port=<port>   Set the listening port (default: 5000)");
    Console.WriteLine("  --headless      Run in server-only mode (no browser needed)");
    Console.WriteLine("  --help, -h      Show this help message");
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);

// Set content root to exe directory (important for published deployment)
builder.Environment.ContentRootPath = AppContext.BaseDirectory;
builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

// Configure JSON serialization for AOT (HTTP endpoints)
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
});

// Register singleton services
builder.Services.AddSingleton<ConnectionManager>();
builder.Services.AddSingleton<GameLoop>(sp =>
{
    var cm = sp.GetRequiredService<ConnectionManager>();
    return new GameLoop(cm);
});
builder.Services.AddSingleton<SessionManager>(sp =>
{
    var cm = sp.GetRequiredService<ConnectionManager>();
    var gl = sp.GetRequiredService<GameLoop>();
    return new SessionManager(cm, gl);
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

// Cross-reference session manager in game loop (for game over/victory detection)
gameLoop.Session = sessionManager;

// Start the game loop
gameLoop.Start();

// Wire up message handling
connectionManager.OnMessageReceived += async (senderId, message) =>
{
    switch (message.Type)
    {
        case MessageTypes.Chat:
            await connectionManager.BroadcastExceptAsync(senderId, message);
            break;

        case MessageTypes.Ping:
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
            if (message.PlayerInput != null)
            {
                gameLoop.InputQueue.Enqueue(senderId, message.PlayerInput);
            }
            break;

        case MessageTypes.SessionAction:
            sessionManager.HandleMessage(senderId, message);
            break;
    }
};

// Handle player connections/disconnections via SessionManager
connectionManager.OnPlayerConnected += (playerId, playerName) =>
{
    sessionManager.AddPlayer(playerId, playerName);
};

connectionManager.OnPlayerDisconnected += (playerId) =>
{
    sessionManager.RemovePlayer(playerId);
};

// Enable WebSockets
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

// WebSocket endpoint
app.Map("/ws", async (HttpContext context, ConnectionManager connectionManager, SessionManager sessionManager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connections only");
        return;
    }

    var playerName = context.Request.Query["name"].FirstOrDefault() ?? "Unknown";
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

    // Send the player their assigned ID
    await connectionManager.SendToAsync(playerId, new GameMessage
    {
        Type = MessageTypes.PlayerJoined,
        PlayerJoined = new PlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName
        }
    });

    // Notify all others
    await connectionManager.BroadcastExceptAsync(playerId, new GameMessage
    {
        Type = MessageTypes.PlayerJoined,
        PlayerJoined = new PlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName
        }
    });

    // Handle messages until disconnect
    await connectionManager.HandleConnectionAsync(playerId, context.RequestAborted);

    Console.WriteLine($"[WS] Player disconnected: {playerName} ({playerId})");

    // Notify others of disconnect
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

// Health check endpoint
app.MapGet("/api/health", (ConnectionManager cm, GameLoop gl, SessionManager sm) => new HealthResponse(
    "Carcosa Server",
    "1.0.0",
    DateTime.UtcNow,
    cm.ConnectionCount,
    gl.State.Tick,
    gl.IsRunning,
    sm.State.ToString()));

// Map info endpoint
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

// Serve static files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

// Fallback: serve index.html for SPA client-side routing
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

// Graceful shutdown
app.Lifetime.ApplicationStopping.Register(() =>
{
    gameLoop.Dispose();
});

Console.WriteLine("===========================================");
Console.WriteLine("  CARCOSA - The King in Yellow");
Console.WriteLine($"  Server running on http://0.0.0.0:{port}");
Console.WriteLine($"  WebSocket endpoint: ws://0.0.0.0:{port}/ws");
Console.WriteLine($"  Game loop: {GameLoop.TickRate} ticks/sec");
if (headless) Console.WriteLine("  Mode: Headless (server only)");
else Console.WriteLine($"  Open http://localhost:{port} in your browser");
Console.WriteLine("===========================================");

app.Run();

// --- AOT-compatible types for HTTP API ---

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

[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(MapInfoResponse))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
