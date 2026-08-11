// =============================================================================
// Program.cs — CARCOSA Matchmaking Service Entry Point
// =============================================================================
//
// PURPOSE:
// Centralized service that handles:
//   1. Player registration (generate persistent player IDs)
//   2. Cryptol balance management (query and update currency)
//   3. Session discovery (game servers publish heartbeats via Kafka)
//   4. Invader matchmaking (find lowest-latency available session)
//
// ARCHITECTURE:
// This runs as a separate process from the game server. Game servers connect
// to it to register their sessions. Players connect to it to find games.
//   - REST API for player/session queries
//   - Kafka consumer for session heartbeat ingestion
//   - Kafka producer for session event publication
//
// DEPLOYMENT:
// Run via docker-compose alongside a Kafka broker (KRaft mode, no Zookeeper).
// See bbq/docker-compose.yml for the local development setup.
//
// FUTURE:
// This service will eventually integrate with Steam for player identity
// and persist Cryptol in a proper database. For now, it uses a JSON file.
// =============================================================================

using System.Text.Json.Serialization;
using Carcosa.Matchmaking.Services;

var port = 5100;
var portArg = args.FirstOrDefault(a => a.StartsWith("--port="));
if (portArg != null && int.TryParse(portArg.AsSpan(7), out var customPort))
{
    port = customPort;
}

var kafkaBroker = args.FirstOrDefault(a => a.StartsWith("--kafka="))?.Substring(8) ?? "localhost:9092";
var headless = args.Contains("--headless");

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("CARCOSA Matchmaking Service");
    Console.WriteLine();
    Console.WriteLine("Usage: Carcosa.Matchmaking [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --port=<port>        Set the listening port (default: 5100)");
    Console.WriteLine("  --kafka=<broker>     Kafka broker address (default: localhost:9092)");
    Console.WriteLine("  --headless           Run without dashboard window (API only)");
    Console.WriteLine("  --help, -h           Show this help message");
    return;
}

var builder = WebApplication.CreateSlimBuilder(args);

// Set content root to exe directory (critical for published deployment to find wwwroot/)
builder.Environment.ContentRootPath = AppContext.BaseDirectory;
builder.Environment.WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");

// Configure AOT-compatible JSON serialization
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, MatchmakingJsonContext.Default);
});

// Register services
builder.Services.AddSingleton(new PlayerStore());
builder.Services.AddSingleton(new SessionRegistry());
builder.Services.AddSingleton(new KafkaService(kafkaBroker));
builder.Services.AddSingleton(new AnalyticsService());

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port);
});

var app = builder.Build();

// Start Kafka consumer for session heartbeats (non-critical — REST fallback works)
var kafka = app.Services.GetRequiredService<KafkaService>();
var sessionRegistry = app.Services.GetRequiredService<SessionRegistry>();
_ = Task.Run(() =>
{
    try { kafka.ConsumeSessionHeartbeats(sessionRegistry); }
    catch (Exception ex) { Console.WriteLine($"[Kafka] Consumer crashed: {ex.Message}. REST heartbeats still work."); }
});

// --- REST API Endpoints ---

// Player Registration: Generate a new player ID
app.MapPost("/api/register", (PlayerStore store) =>
{
    var player = store.RegisterPlayer();
    return Results.Ok(player);
});

// Get Player Info (balance, etc.)
app.MapGet("/api/player/{id}", (string id, PlayerStore store) =>
{
    var player = store.GetPlayer(id);
    return player != null ? Results.Ok(player) : Results.NotFound();
});

// Update Cryptol Balance
app.MapPost("/api/player/{id}/cryptol", (string id, CryptolUpdateRequest request, PlayerStore store) =>
{
    var player = store.UpdateCryptol(id, request.Amount);
    return player != null ? Results.Ok(player) : Results.NotFound();
});

// List Active Sessions
app.MapGet("/api/sessions", (SessionRegistry registry) =>
{
    return Results.Ok(registry.GetActiveSessions());
});

// Get Best Session (lowest player count for invader matching)
app.MapGet("/api/sessions/best", (SessionRegistry registry) =>
{
    var session = registry.GetBestSession();
    return session != null ? Results.Ok(session) : Results.NotFound();
});

// Health Check
app.MapGet("/api/health", () => new HealthResponse("Carcosa Matchmaking", "1.0.0", DateTime.UtcNow));

// Session Heartbeat via REST (alternative to Kafka for simple setups)
app.MapPost("/api/sessions/heartbeat", (SessionHeartbeat heartbeat, SessionRegistry registry) =>
{
    registry.UpdateSession(heartbeat);
    return Results.Ok();
});

// List all registered players (for dashboard)
app.MapGet("/api/players", (PlayerStore store) =>
{
    return Results.Ok(store.GetAllPlayers());
});

// Analytics: aggregate stats for the dashboard
app.MapGet("/api/analytics", (AnalyticsService analytics, PlayerStore store) =>
{
    var data = analytics.GetAnalytics(store.GetAllPlayers().Count);
    return Results.Ok(data);
});

// Report a completed match result (called by game servers)
app.MapPost("/api/match-result", (MatchResult result, AnalyticsService analytics) =>
{
    analytics.RecordMatch(result);
    return Results.Ok();
});

Console.WriteLine("===========================================");
Console.WriteLine("  CARCOSA Matchmaking Service");
Console.WriteLine($"  REST API: http://0.0.0.0:{port}");
Console.WriteLine($"  Kafka broker: {kafkaBroker}");
if (headless) Console.WriteLine("  Mode: Headless (API only)");
else Console.WriteLine($"  Dashboard: http://localhost:{port}");
Console.WriteLine("===========================================");

// Serve static dashboard files from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallback(async context =>
{
    var indexPath = Path.Combine(app.Environment.WebRootPath ?? "", "index.html");
    if (File.Exists(indexPath))
    {
        context.Response.ContentType = "text/html";
        await context.Response.SendFileAsync(indexPath);
    }
    else context.Response.StatusCode = 404;
});

if (headless)
{
    app.Run();
}
else
{
    // Windowed: start web server, then open Edge in "app mode" for the dashboard
    _ = Task.Run(() =>
    {
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

        // Launch Edge in app mode
        var edgePath = FindEdgePath();
        if (edgePath != null)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = edgePath,
                Arguments = $"--app=http://localhost:{port} --window-size=1400,900 --disable-extensions",
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
            Console.WriteLine("[Window] Launched dashboard in Edge app mode");
        }
        else
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = $"http://localhost:{port}",
                UseShellExecute = true,
            });
            Console.WriteLine("[Window] Launched dashboard in default browser");
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

// --- Request/Response types ---

internal record HealthResponse(string Name, string Version, DateTime Timestamp);
internal record CryptolUpdateRequest(int Amount);

// --- AOT JSON Context ---
[JsonSerializable(typeof(HealthResponse))]
[JsonSerializable(typeof(CryptolUpdateRequest))]
[JsonSerializable(typeof(PlayerInfo))]
[JsonSerializable(typeof(PlayerInfo[]))]
[JsonSerializable(typeof(List<PlayerInfo>))]
[JsonSerializable(typeof(SessionHeartbeat))]
[JsonSerializable(typeof(SessionHeartbeat[]))]
[JsonSerializable(typeof(List<SessionHeartbeat>))]
[JsonSerializable(typeof(AnalyticsData))]
[JsonSerializable(typeof(ClassDistribution))]
[JsonSerializable(typeof(ScenarioDistribution))]
[JsonSerializable(typeof(MatchResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class MatchmakingJsonContext : JsonSerializerContext { }
