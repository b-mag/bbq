// =============================================================================
// Program.cs — CARCOSA Headless Bot Client
// =============================================================================
//
// PURPOSE:
// A standalone tool that connects one or more AI-controlled bot players to a
// game server via WebSocket. Bots simulate real player behavior:
//   - Connect with a name, select a class, ready up
//   - In-game: move toward enemies, fire weapons, use med kits when low
//   - Participate in the full game lifecycle (lobby → game → end)
//
// USAGE:
//   BotClient.exe --server=ws://localhost:5000 --count=2 --names=Alpha,Bravo
//
// WHY A SEPARATE TOOL:
// Testing multiplayer gameplay requires multiple connected players. Bots let a
// solo developer test the full game flow without needing other humans. They also
// serve as load-test clients for the WebSocket infrastructure.
//
// BOT BEHAVIOR:
//   - Lobby: Select random class, ready up after 2 seconds
//   - In-game: Every tick (50ms), analyze game state:
//     * If enemy nearby: aim at it and fire
//     * If no enemy visible: move in a random direction (patrol)
//     * If HP below 30% and has med kits: use one
//   - Bots don't use secondary abilities or interact (simple behavior)
// =============================================================================

using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

// --- Parse CLI arguments ---
var serverUrl = "ws://localhost:5000";
var botCount = 1;
var names = new List<string>();

foreach (var arg in args)
{
    if (arg.StartsWith("--server=")) serverUrl = arg[9..];
    else if (arg.StartsWith("--count=") && int.TryParse(arg[8..], out var c)) botCount = c;
    else if (arg.StartsWith("--names=")) names.AddRange(arg[8..].Split(','));
}

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.WriteLine("CARCOSA Bot Client");
    Console.WriteLine();
    Console.WriteLine("Usage: Carcosa.BotClient [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --server=<url>     WebSocket server URL (default: ws://localhost:5000)");
    Console.WriteLine("  --count=<n>        Number of bots to spawn (default: 1)");
    Console.WriteLine("  --names=<a,b,c>    Comma-separated bot names (default: Bot_1, Bot_2, ...)");
    Console.WriteLine("  --help, -h         Show this help message");
    return;
}

// Fill in default names
while (names.Count < botCount)
{
    names.Add($"Bot_{names.Count + 1}");
}

Console.WriteLine($"[BotClient] Starting {botCount} bot(s) connecting to {serverUrl}");

// Launch all bots concurrently
var tasks = new List<Task>();
for (int i = 0; i < botCount; i++)
{
    var botName = names[i];
    var botIndex = i;
    tasks.Add(Task.Run(() => RunBot(serverUrl, botName, botIndex)));
}

await Task.WhenAll(tasks);
Console.WriteLine("[BotClient] All bots finished.");

// =============================================================================
// Bot Logic
// =============================================================================

static async Task RunBot(string serverUrl, string name, int index)
{
    var rng = new Random(index * 31337);
    var classes = new[] { "gangster", "detective", "surgeon" };
    var selectedClass = classes[rng.Next(classes.Length)];

    Console.WriteLine($"[{name}] Connecting as {selectedClass}...");

    try
    {
        using var ws = new ClientWebSocket();
        var wsUrl = $"{serverUrl}/ws?name={Uri.EscapeDataString(name)}";
        await ws.ConnectAsync(new Uri(wsUrl), CancellationToken.None);
        Console.WriteLine($"[{name}] Connected!");

        string? myPlayerId = null;
        var inGame = false;
        var sequenceNumber = 0;
        float myX = 0, myY = 0;
        var entities = new List<EntityData>();

        // Start receive loop in background
        var receiveCts = new CancellationTokenSource();
        var receiveTask = Task.Run(async () =>
        {
            var buffer = new byte[8192];
            while (!receiveCts.Token.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(buffer, receiveCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType != WebSocketMessageType.Text) continue;

                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var msg = JsonSerializer.Deserialize(json, BotJsonContext.Default.BotMessage);
                    if (msg == null) continue;

                    switch (msg.Type)
                    {
                        case "player_joined":
                            if (myPlayerId == null && msg.PlayerJoined != null)
                            {
                                myPlayerId = msg.PlayerJoined.PlayerId;
                                Console.WriteLine($"[{name}] Assigned ID: {myPlayerId}");
                            }
                            break;
                        case "map_data":
                            inGame = true;
                            Console.WriteLine($"[{name}] Game started!");
                            break;
                        case "game_state":
                            if (msg.GameState?.Entities != null)
                            {
                                entities = msg.GameState.Entities.ToList();
                                // Update my position
                                var me = entities.FirstOrDefault(e => e.Id == $"player_{myPlayerId}");
                                if (me != null) { myX = me.X; myY = me.Y; }
                            }
                            break;
                        case "game_event":
                            if (msg.GameEvent?.Event is "game_over" or "victory")
                            {
                                Console.WriteLine($"[{name}] Game ended: {msg.GameEvent.Event}");
                                inGame = false;
                            }
                            break;
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (WebSocketException) { break; }
            }
        });

        // Wait a moment then select class and ready up
        await Task.Delay(1000 + rng.Next(1000));
        await SendAction(ws, "select_class", selectedClass);
        await Task.Delay(1000 + rng.Next(1000));
        await SendAction(ws, "set_ready", "true");
        Console.WriteLine($"[{name}] Ready ({selectedClass})");

        // Main bot loop: run at 20Hz (matching server tick rate)
        while (ws.State == WebSocketState.Open)
        {
            await Task.Delay(50);

            if (!inGame || myPlayerId == null) continue;

            // Find my entity
            var myEntity = entities.FirstOrDefault(e => e.Id == $"player_{myPlayerId}");
            if (myEntity == null || !myEntity.IsAlive) continue;

            // Find nearest enemy
            EntityData? nearestEnemy = null;
            float nearestDist = float.MaxValue;
            foreach (var e in entities)
            {
                if (e.EntityType != "enemy" || !e.IsAlive) continue;
                var dx = e.X - myX;
                var dy = e.Y - myY;
                var dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestEnemy = e;
                }
            }

            float moveX = 0, moveY = 0;
            float aimAngle = rng.NextSingle() * MathF.PI * 2;
            bool fire = false;
            bool useMedKit = false;

            if (nearestEnemy != null && nearestDist < 12f)
            {
                // Move toward enemy and fire
                var dx = nearestEnemy.X - myX;
                var dy = nearestEnemy.Y - myY;
                aimAngle = MathF.Atan2(dy, dx);
                fire = true;

                if (nearestDist > 3f)
                {
                    // Move toward
                    var mag = MathF.Sqrt(dx * dx + dy * dy);
                    moveX = dx / mag;
                    moveY = dy / mag;
                }
            }
            else
            {
                // Patrol: random movement
                if (rng.Next(20) == 0) // Change direction every ~1 second
                {
                    aimAngle = rng.NextSingle() * MathF.PI * 2;
                }
                moveX = MathF.Cos(aimAngle) * 0.5f;
                moveY = MathF.Sin(aimAngle) * 0.5f;
            }

            // Use med kit if health is low
            if (myEntity.Health < myEntity.MaxHealth * 0.3f && myEntity.MedKits > 0)
            {
                useMedKit = true;
            }

            sequenceNumber++;
            await SendInput(ws, sequenceNumber, moveX, moveY, fire, useMedKit, aimAngle);
        }

        receiveCts.Cancel();
        await receiveTask;
        Console.WriteLine($"[{name}] Disconnected.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[{name}] Error: {ex.Message}");
    }
}

static async Task SendAction(ClientWebSocket ws, string action, string value)
{
    var msg = $"{{\"type\":\"session_action\",\"sessionAction\":{{\"action\":\"{action}\",\"value\":\"{value}\"}}}}";
    await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
}

static async Task SendInput(ClientWebSocket ws, int seq, float moveX, float moveY, bool fire, bool useMedKit, float aimAngle)
{
    var msg = $"{{\"type\":\"player_input\",\"playerInput\":{{\"sequenceNumber\":{seq},\"moveX\":{moveX:F2},\"moveY\":{moveY:F2},\"primaryFire\":{(fire ? "true" : "false")},\"secondaryAbility\":false,\"interact\":false,\"useMedKit\":{(useMedKit ? "true" : "false")},\"aimAngle\":{aimAngle:F3},\"timestamp\":{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}}}";
    await ws.SendAsync(Encoding.UTF8.GetBytes(msg), WebSocketMessageType.Text, true, CancellationToken.None);
}

// =============================================================================
// Minimal message types for bot JSON parsing (only what we need to read)
// =============================================================================

internal sealed class BotMessage
{
    public string Type { get; set; } = "";
    public BotPlayerJoined? PlayerJoined { get; set; }
    public BotGameState? GameState { get; set; }
    public BotGameEvent? GameEvent { get; set; }
}

internal sealed class BotPlayerJoined
{
    public string PlayerId { get; set; } = "";
}

internal sealed class BotGameState
{
    public EntityData[]? Entities { get; set; }
}

internal sealed class BotGameEvent
{
    public string Event { get; set; } = "";
}

internal sealed class EntityData
{
    public string Id { get; set; } = "";
    public string EntityType { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public int Health { get; set; }
    public int MaxHealth { get; set; }
    public int MedKits { get; set; }
    public bool IsAlive { get; set; } = true;
}

[JsonSerializable(typeof(BotMessage))]
[JsonSerializable(typeof(BotPlayerJoined))]
[JsonSerializable(typeof(BotGameState))]
[JsonSerializable(typeof(BotGameEvent))]
[JsonSerializable(typeof(EntityData))]
[JsonSerializable(typeof(EntityData[]))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class BotJsonContext : JsonSerializerContext { }
