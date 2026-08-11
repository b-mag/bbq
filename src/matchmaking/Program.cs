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
using Carcosa.Matchmaking.Overworld;

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
builder.Services.AddSingleton(new OverworldMapStore());
builder.Services.AddSingleton<OverworldConnectionManager>();
builder.Services.AddSingleton(sp => new OverworldLoop(
    sp.GetRequiredService<OverworldConnectionManager>(),
    sp.GetRequiredService<OverworldMapStore>()));
builder.Services.AddSingleton<PartyManager>();
builder.Services.AddSingleton(sp => new ChatManager(
    sp.GetRequiredService<OverworldConnectionManager>(),
    sp.GetRequiredService<OverworldLoop>(),
    sp.GetRequiredService<PartyManager>()));

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(port);
});

var app = builder.Build();

// Initialize the overworld map on startup (ensure it exists)
var mapStore = app.Services.GetRequiredService<OverworldMapStore>();
var overworldMap = mapStore.GetMap();
Console.WriteLine($"[Overworld] Map ready: {overworldMap.Width}x{overworldMap.Height}, " +
    $"{overworldMap.Landmarks.Count} landmarks, {overworldMap.DungeonEntrances.Count} dungeon entrances, " +
    $"{overworldMap.WorldObjects.Count} world objects");

// Start Kafka consumer for session heartbeats (non-critical — REST fallback works)
var kafka = app.Services.GetRequiredService<KafkaService>();
var sessionRegistry = app.Services.GetRequiredService<SessionRegistry>();
_ = Task.Run(() =>
{
    try { kafka.ConsumeSessionHeartbeats(sessionRegistry); }
    catch (Exception ex) { Console.WriteLine($"[Kafka] Consumer crashed: {ex.Message}. REST heartbeats still work."); }
});

// --- WebSocket Support for Overworld ---
app.UseWebSockets();

var owConnections = app.Services.GetRequiredService<OverworldConnectionManager>();
var owLoop = app.Services.GetRequiredService<OverworldLoop>();
var partyManager = app.Services.GetRequiredService<PartyManager>();
var chatManager = app.Services.GetRequiredService<ChatManager>();

// Wire up overworld events
owConnections.OnPlayerConnected += (playerId, playerName) =>
{
    var player = owLoop.AddPlayer(playerId, playerName);
    Console.WriteLine($"[Overworld] {playerName} connected at ({player.X:F1}, {player.Y:F1})");

    // Send map data to the new player
    var map = mapStore.GetMap();
    var mapMsg = new OverworldMessage
    {
        Type = OwMessageTypes.MapData,
        MapData = new OwMapDataPayload
        {
            Width = map.Width,
            Height = map.Height,
            Seed = map.Seed,
            TilesBase64 = map.TilesBase64,
            SpawnX = map.SpawnPoint.X + 0.5f,
            SpawnY = map.SpawnPoint.Y + 0.5f,
            Landmarks = map.Landmarks.Select(l => new OwLandmarkData
            {
                Name = l.Name, X = l.X, Y = l.Y, Type = l.Type
            }).ToArray(),
            DungeonEntrances = map.DungeonEntrances.Select(e => new OwDungeonEntranceData
            {
                Name = e.Name, X = e.X, Y = e.Y, Scenario = e.Scenario
            }).ToArray(),
            WorldObjects = map.WorldObjects.Select(o => new OwWorldObjectData
            {
                Type = o.Type, X = o.X, Y = o.Y, Collision = o.Collision, CollisionRadius = o.CollisionRadius
            }).ToArray(),
        }
    };
    _ = owConnections.SendToAsync(playerId, mapMsg);

    // Send player_joined to the new player (their identity)
    _ = owConnections.SendToAsync(playerId, new OverworldMessage
    {
        Type = OwMessageTypes.PlayerJoined,
        PlayerJoined = new OwPlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName,
            X = player.X,
            Y = player.Y
        }
    });

    // Broadcast join to all other players
    _ = owConnections.BroadcastExceptAsync(playerId, new OverworldMessage
    {
        Type = OwMessageTypes.PlayerJoined,
        PlayerJoined = new OwPlayerJoinedPayload
        {
            PlayerId = playerId,
            PlayerName = playerName,
            X = player.X,
            Y = player.Y
        }
    });

    // Send existing players to the new player
    foreach (var existingPlayer in owLoop.Players.Values)
    {
        if (existingPlayer.Id == playerId) continue;
        if (existingPlayer.Status == "in_dungeon") continue;
        _ = owConnections.SendToAsync(playerId, new OverworldMessage
        {
            Type = OwMessageTypes.PlayerJoined,
            PlayerJoined = new OwPlayerJoinedPayload
            {
                PlayerId = existingPlayer.Id,
                PlayerName = existingPlayer.Name,
                X = existingPlayer.X,
                Y = existingPlayer.Y
            }
        });
    }
};

owConnections.OnPlayerDisconnected += (playerId) =>
{
    var player = owLoop.GetPlayer(playerId);
    var name = player?.Name ?? playerId;
    owLoop.RemovePlayer(playerId);
    Console.WriteLine($"[Overworld] {name} disconnected");

    // Clean up party membership
    var (party, disbanded) = partyManager.HandleDisconnect(playerId);
    if (party != null && !disbanded)
    {
        // Notify remaining party members
        var update = new OverworldMessage
        {
            Type = OwMessageTypes.PartyUpdate,
            PartyUpdate = new OwPartyUpdatePayload
            {
                PartyId = party.Id,
                LeaderId = party.LeaderId,
                Members = party.MemberIds.Select(id =>
                {
                    var p = owLoop.GetPlayer(id);
                    return new OwPartyMember { Id = id, Name = p?.Name ?? id, IsLeader = id == party.LeaderId };
                }).ToArray(),
                Event = "left",
            }
        };
        _ = owConnections.SendToMultipleAsync(party.MemberIds, update);
    }
    else if (party != null && disbanded)
    {
        // Notify last member that party is disbanded
        _ = owConnections.BroadcastAsync(new OverworldMessage
        {
            Type = OwMessageTypes.PartyUpdate,
            PartyUpdate = new OwPartyUpdatePayload
            {
                PartyId = party.Id,
                LeaderId = "",
                Members = Array.Empty<OwPartyMember>(),
                Event = "disbanded",
            }
        });
    }

    // Update overworld player states (clear party info)
    foreach (var p in owLoop.Players.Values)
    {
        if (p.PartyId == party?.Id)
        {
            var currentParty = partyManager.GetPlayerParty(p.Id);
            p.PartyId = currentParty?.Id;
            p.IsPartyLeader = currentParty?.LeaderId == p.Id;
            p.IsDirty = true;
        }
    }

    _ = owConnections.BroadcastAsync(new OverworldMessage
    {
        Type = OwMessageTypes.PlayerLeft,
        PlayerLeft = new OwPlayerLeftPayload { PlayerId = playerId, Reason = "disconnected" }
    });
};

owConnections.OnMessageReceived += (playerId, message) =>
{
    switch (message.Type)
    {
        case OwMessageTypes.PlayerInput when message.PlayerInput != null:
            owLoop.QueueInput(playerId, message.PlayerInput);
            // Check for interact near dungeon entrance
            if (message.PlayerInput.Interact)
            {
                HandleDungeonInteract(playerId);
            }
            break;

        case OwMessageTypes.Ping when message.Ping != null:
            _ = owConnections.SendToAsync(playerId, new OverworldMessage
            {
                Type = OwMessageTypes.Pong,
                Pong = new OwPongPayload
                {
                    ClientTimestamp = message.Ping.ClientTimestamp,
                    ServerTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                }
            });
            break;

        // --- Party System Messages ---

        case OwMessageTypes.PartyInvite when message.PartyInvite != null:
        {
            // Player is inviting another player (target ID is sent in InviterId field as a workaround;
            // the client sends the target player ID they clicked on)
            var targetId = message.PartyInvite.InviterId; // Reuse field: client sends targetId here
            var (partyId, success, error) = partyManager.InvitePlayer(playerId, targetId);

            if (success && partyId != null)
            {
                var inviterPlayer = owLoop.GetPlayer(playerId);
                // Send invite to target
                _ = owConnections.SendToAsync(targetId, new OverworldMessage
                {
                    Type = OwMessageTypes.PartyInvite,
                    PartyInvite = new OwPartyInvitePayload
                    {
                        PartyId = partyId,
                        InviterId = playerId,
                        InviterName = inviterPlayer?.Name ?? playerId,
                    }
                });

                // Update inviter's player state
                var inviterState = owLoop.GetPlayer(playerId);
                if (inviterState != null)
                {
                    inviterState.PartyId = partyId;
                    inviterState.IsPartyLeader = true;
                    inviterState.Status = "in_party";
                    inviterState.IsDirty = true;
                }

                // Send party update to inviter
                var party = partyManager.GetParty(partyId);
                if (party != null)
                {
                    _ = owConnections.SendToAsync(playerId, new OverworldMessage
                    {
                        Type = OwMessageTypes.PartyUpdate,
                        PartyUpdate = new OwPartyUpdatePayload
                        {
                            PartyId = partyId,
                            LeaderId = party.LeaderId,
                            Members = party.MemberIds.Select(id =>
                            {
                                var p = owLoop.GetPlayer(id);
                                return new OwPartyMember { Id = id, Name = p?.Name ?? id, IsLeader = id == party.LeaderId };
                            }).ToArray(),
                            Event = "formed",
                        }
                    });
                }
            }
            else if (error != null)
            {
                _ = owConnections.SendToAsync(playerId, new OverworldMessage
                {
                    Type = OwMessageTypes.Error,
                    Error = new OwErrorPayload { Code = "party_invite_failed", Message = error }
                });
            }
            break;
        }

        case OwMessageTypes.PartyResponse when message.PartyResponse != null:
        {
            var response = message.PartyResponse;
            if (response.Accepted)
            {
                var (party, success, error) = partyManager.AcceptInvite(playerId, response.PartyId);
                if (success && party != null)
                {
                    // Update player state
                    var playerState = owLoop.GetPlayer(playerId);
                    if (playerState != null)
                    {
                        playerState.PartyId = party.Id;
                        playerState.IsPartyLeader = false;
                        playerState.Status = "in_party";
                        playerState.IsDirty = true;
                    }

                    // Broadcast party update to all members
                    var update = new OverworldMessage
                    {
                        Type = OwMessageTypes.PartyUpdate,
                        PartyUpdate = new OwPartyUpdatePayload
                        {
                            PartyId = party.Id,
                            LeaderId = party.LeaderId,
                            Members = party.MemberIds.Select(id =>
                            {
                                var p = owLoop.GetPlayer(id);
                                return new OwPartyMember { Id = id, Name = p?.Name ?? id, IsLeader = id == party.LeaderId };
                            }).ToArray(),
                            Event = "joined",
                        }
                    };
                    _ = owConnections.SendToMultipleAsync(party.MemberIds, update);
                }
            }
            else
            {
                partyManager.DeclineInvite(playerId, response.PartyId);
            }
            break;
        }

        // --- Chat Message ---
        case OwMessageTypes.ChatMessage when message.ChatMessage != null:
            _ = chatManager.HandleChatMessage(playerId, message.ChatMessage);
            break;

        // --- Dungeon Complete (player returned from dungeon) ---
        case OwMessageTypes.DungeonComplete when message.DungeonComplete != null:
        {
            var player = owLoop.GetPlayer(playerId);
            if (player != null && player.Status == "in_dungeon")
            {
                player.Status = player.PartyId != null ? "in_party" : "exploring";
                player.IsDirty = true;
                Console.WriteLine($"[Dungeon] {player.Name} returned from dungeon");
            }
            break;
        }
    }
};

// --- Dungeon Entry Logic ---
void HandleDungeonInteract(string playerId)
{
    var player = owLoop.GetPlayer(playerId);
    if (player == null) return;

    // Check if near a dungeon entrance (within 2.5 tiles)
    var entrance = overworldMap.DungeonEntrances.FirstOrDefault(e =>
    {
        var dx = e.X - player.X;
        var dy = e.Y - player.Y;
        return MathF.Sqrt(dx * dx + dy * dy) < 2.5f;
    });

    if (entrance == null) return;

    // Must be party leader or solo (not in a party)
    var party = partyManager.GetPlayerParty(playerId);
    if (party != null && party.LeaderId != playerId)
    {
        _ = owConnections.SendToAsync(playerId, new OverworldMessage
        {
            Type = OwMessageTypes.Error,
            Error = new OwErrorPayload { Code = "not_leader", Message = "Only the party leader can enter dungeons" }
        });
        return;
    }

    // Generate a dungeon seed
    var seed = Random.Shared.Next();
    var memberIds = party?.MemberIds ?? new List<string> { playerId };

    Console.WriteLine($"[Dungeon] {player.Name} entering {entrance.Name} (seed: {seed}, party: {memberIds.Count} members)");

    // Send dungeon_prepare to the party leader (they host the instance)
    _ = owConnections.SendToAsync(playerId, new OverworldMessage
    {
        Type = OwMessageTypes.DungeonPrepare,
        DungeonPrepare = new OwDungeonPreparePayload
        {
            Seed = seed,
            Scenario = entrance.Scenario,
            DungeonWidth = entrance.DungeonWidth,
            DungeonHeight = entrance.DungeonHeight,
            PartyMemberIds = memberIds.ToArray(),
        }
    });

    // Send dungeon_connect to all party members (including leader)
    // The leader's client will start a local server and all members connect to it.
    // For now, we send the leader's connection info (their client will report back the address)
    var connectMsg = new OverworldMessage
    {
        Type = OwMessageTypes.DungeonConnect,
        DungeonConnect = new OwDungeonConnectPayload
        {
            HostAddress = $"localhost:5000", // Leader's local server address (default port)
            Seed = seed,
            Scenario = entrance.Scenario,
        }
    };
    _ = owConnections.SendToMultipleAsync(memberIds, connectMsg);

    // Mark all party members as in_dungeon
    foreach (var memberId in memberIds)
    {
        var memberPlayer = owLoop.GetPlayer(memberId);
        if (memberPlayer != null)
        {
            memberPlayer.Status = "in_dungeon";
            memberPlayer.IsDirty = true;
        }
    }
}

// WebSocket endpoint for overworld connections
app.Map("/ws/overworld", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        await context.Response.WriteAsync("WebSocket connection required");
        return;
    }

    var name = context.Request.Query["name"].FirstOrDefault() ?? "Unknown";
    var playerId = Guid.NewGuid().ToString("N")[..8];

    var ws = await context.WebSockets.AcceptWebSocketAsync();

    if (!owConnections.TryAddConnection(playerId, name, ws))
    {
        await ws.CloseAsync(
            System.Net.WebSockets.WebSocketCloseStatus.PolicyViolation,
            "Connection rejected", CancellationToken.None);
        return;
    }

    await owConnections.HandleConnectionAsync(playerId, context.RequestAborted);
});

// Start the overworld game loop
owLoop.Start();

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

// --- Overworld Map API ---

// Get the overworld map data (tiles + metadata)
app.MapGet("/api/overworld/map", (OverworldMapStore store) =>
{
    var map = store.GetMap();
    return Results.Ok(map);
});

// Regenerate the overworld map (admin action)
app.MapPost("/api/overworld/regenerate", (OverworldMapStore store) =>
{
    var map = store.Regenerate();
    return Results.Ok(new SimpleMessageResult { Message = "Overworld regenerated", Seed = map.Seed });
});

// Reload overworld from disk (picks up manual JSON edits)
app.MapPost("/api/overworld/reload", (OverworldMapStore store) =>
{
    var map = store.Reload();
    return map != null
        ? Results.Ok(new SimpleMessageResult { Message = "Overworld reloaded from disk", Seed = map.Seed })
        : Results.Problem("Failed to reload overworld map");
});

// --- Overworld Live Metrics API (for Dashboard) ---

// Get all players currently in the overworld
app.MapGet("/api/overworld/players", () =>
{
    var players = owLoop.Players.Values
        .Where(p => p.Status != "in_dungeon")
        .Select(p => new OverworldPlayerResult
        {
            Id = p.Id, Name = p.Name, X = p.X, Y = p.Y, Status = p.Status, PartyId = p.PartyId ?? "",
        })
        .ToArray();
    return Results.Ok(players);
});

// Get all active parties
app.MapGet("/api/overworld/parties", () =>
{
    var parties = partyManager.GetAllParties().Select(p => new OverworldPartyResult
    {
        Id = p.Id,
        LeaderId = p.LeaderId,
        LeaderName = owLoop.GetPlayer(p.LeaderId)?.Name ?? p.LeaderId,
        MemberCount = p.MemberIds.Count,
        Members = p.MemberIds.Select(mid => new OverworldPartyMemberResult
        {
            Id = mid, Name = owLoop.GetPlayer(mid)?.Name ?? mid,
        }).ToArray(),
        Status = p.MemberIds.Any(mid => owLoop.GetPlayer(mid)?.Status == "in_dungeon") ? "in_dungeon" : "exploring",
    }).ToArray();
    return Results.Ok(parties);
});

// Get overworld summary stats
app.MapGet("/api/overworld/stats", () =>
{
    var allPlayers = owLoop.Players.Values.ToList();
    var stats = new OverworldStatsResult
    {
        TotalConnected = owConnections.ConnectionCount,
        InOverworld = allPlayers.Count(p => p.Status != "in_dungeon"),
        InDungeon = allPlayers.Count(p => p.Status == "in_dungeon"),
        InParties = allPlayers.Count(p => p.PartyId != null),
        TotalParties = partyManager.GetAllParties().Count,
    };
    return Results.Ok(stats);
});

// =============================================================================
// P2P TRACKER API — Optional peer discovery service
// =============================================================================
// Peers register here to be discoverable. When a new peer starts, it queries
// the tracker for peers in its world shard. All tracker operations are optional —
// the P2P mesh works without the tracker via PEX and cached peers.

// In-memory peer registry (not persisted — peers re-register on heartbeat)
var trackerPeers = new Dictionary<string, TrackerPeerEntry>();
var trackerLock = new object();
var adminMessages = new List<AdminBroadcastEntry>(); // Pending admin messages
var adminMessageLock = new object();

// Register a peer and return peers in the same world
app.MapPost("/api/tracker/register", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var reg = System.Text.Json.JsonSerializer.Deserialize(body, MatchmakingTrackerJsonContext.Default.TrackerPeerEntry);

    if (reg == null || string.IsNullOrEmpty(reg.PeerId))
        return Results.BadRequest("Invalid registration");

    lock (trackerLock)
    {
        reg.LastSeen = DateTime.UtcNow;
        trackerPeers[reg.PeerId] = reg;

        // Prune stale peers (not seen in 60 seconds)
        var stale = trackerPeers.Where(kv => (DateTime.UtcNow - kv.Value.LastSeen).TotalSeconds > 60)
            .Select(kv => kv.Key).ToList();
        foreach (var key in stale) trackerPeers.Remove(key);

        // Return peers in the same world (or all peers if world is empty)
        var worldPeers = string.IsNullOrEmpty(reg.WorldId)
            ? trackerPeers.Values.ToArray()
            : trackerPeers.Values.Where(p => p.WorldId == reg.WorldId && p.PeerId != reg.PeerId).ToArray();

        // Get any pending admin messages (within last 5 minutes)
        AdminBroadcastEntry[] pendingAdmin;
        lock (adminMessageLock)
        {
            var cutoff = DateTime.UtcNow.AddMinutes(-5);
            pendingAdmin = adminMessages.Where(m => m.CreatedAt > cutoff).ToArray();
        }

        return Results.Ok(new TrackerRegisterResult
        {
            Peers = worldPeers.Select(p => new TrackerPeerResult
            {
                PeerId = p.PeerId, Address = p.Address, DisplayName = p.DisplayName, WorldId = p.WorldId
            }).ToArray(),
            WorldId = reg.WorldId ?? "",
            AdminMessages = pendingAdmin.Select(m => new TrackerAdminResult
            {
                Message = m.Message, Priority = m.Priority, DurationSeconds = m.DurationSeconds,
                Timestamp = m.Timestamp, MessageId = m.MessageId
            }).ToArray(),
        });
    }
});

// Reflect endpoint (STUN-like): returns the caller's public IP address
app.MapGet("/api/tracker/reflect", (HttpContext context) =>
{
    var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    // Normalize loopback addresses to 127.0.0.1
    if (remoteIp == "::1" || remoteIp == "0.0.0.1" || remoteIp == "0.0.0.0")
        remoteIp = "127.0.0.1";
    else if (context.Connection.RemoteIpAddress != null && context.Connection.RemoteIpAddress.IsIPv6LinkLocal)
        remoteIp = context.Connection.RemoteIpAddress.MapToIPv4().ToString();
    var remotePort = context.Connection.RemotePort;
    return Results.Ok(new TrackerReflectResult { Address = remoteIp, Port = remotePort });
});

// Get all registered peers (for dashboard)
app.MapGet("/api/tracker/peers", () =>
{
    lock (trackerLock)
    {
        var peers = trackerPeers.Values.Select(p => new TrackerPeerDashboard
        {
            PeerId = p.PeerId, DisplayName = p.DisplayName, Address = p.Address,
            WorldId = p.WorldId, PlayerCount = p.PlayerCount, LastSeen = p.LastSeen,
        }).ToArray();
        return Results.Ok(peers);
    }
});

// Deregister a peer (on shutdown)
app.MapDelete("/api/tracker/peers/{peerId}", (string peerId) =>
{
    lock (trackerLock)
    {
        trackerPeers.Remove(peerId);
    }
    return Results.Ok();
});

// =============================================================================
// ADMIN BROADCAST — Type a message that all peers will display
// =============================================================================

// Post an admin message (displayed to all connected players)
app.MapPost("/api/admin/broadcast", async (HttpContext context) =>
{
    using var reader = new StreamReader(context.Request.Body);
    var body = await reader.ReadToEndAsync();
    var request = System.Text.Json.JsonSerializer.Deserialize(body, MatchmakingTrackerJsonContext.Default.AdminBroadcastRequest);

    if (request == null || string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new P2PErrorResult { Error = "Message is required" });

    var entry = new AdminBroadcastEntry
    {
        MessageId = Guid.NewGuid().ToString("N")[..8],
        Message = request.Message.Trim(),
        Priority = request.Priority ?? "info",
        DurationSeconds = request.DurationSeconds > 0 ? request.DurationSeconds : 15,
        Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        CreatedAt = DateTime.UtcNow,
    };

    lock (adminMessageLock)
    {
        adminMessages.Add(entry);

        // Keep only last 50 messages
        if (adminMessages.Count > 50)
            adminMessages.RemoveRange(0, adminMessages.Count - 50);
    }

    Console.WriteLine($"[Admin] Broadcast: \"{entry.Message}\" (priority: {entry.Priority})");
    return Results.Ok(new AdminBroadcastResult { MessageId = entry.MessageId, Message = entry.Message });
});

// Get recent admin messages (for dashboard display)
app.MapGet("/api/admin/messages", () =>
{
    lock (adminMessageLock)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var recent = adminMessages.Where(m => m.CreatedAt > cutoff)
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new AdminMessageResult
            {
                MessageId = m.MessageId, Message = m.Message, Priority = m.Priority,
                DurationSeconds = m.DurationSeconds, Timestamp = m.Timestamp
            })
            .ToArray();
        return Results.Ok(recent);
    }
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

/// <summary>
/// A peer registered with the tracker for discovery.
/// </summary>
internal sealed class TrackerPeerEntry
{
    public string PeerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Address { get; set; } = "";
    public string WorldId { get; set; } = "";
    public int PlayerCount { get; set; }
    public string GameVersion { get; set; } = "";
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Request body for POST /api/admin/broadcast.
/// </summary>
internal sealed class AdminBroadcastRequest
{
    public string Message { get; set; } = "";
    public string? Priority { get; set; }
    public int DurationSeconds { get; set; } = 15;
}

/// <summary>
/// A stored admin broadcast message.
/// </summary>
internal sealed class AdminBroadcastEntry
{
    public required string MessageId { get; init; }
    public required string Message { get; init; }
    public string Priority { get; set; } = "info";
    public int DurationSeconds { get; set; } = 15;
    public long Timestamp { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// --- Typed API response records (required for AOT — no anonymous objects) ---

internal sealed class SimpleMessageResult
{
    public string Message { get; set; } = "";
    public int Seed { get; set; }
}

internal sealed class TrackerReflectResult
{
    public string Address { get; set; } = "";
    public int Port { get; set; }
}

internal sealed class TrackerRegisterResult
{
    public TrackerPeerResult[] Peers { get; set; } = Array.Empty<TrackerPeerResult>();
    public string WorldId { get; set; } = "";
    public TrackerAdminResult[] AdminMessages { get; set; } = Array.Empty<TrackerAdminResult>();
}

internal sealed class TrackerPeerResult
{
    public string PeerId { get; set; } = "";
    public string Address { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string WorldId { get; set; } = "";
}

internal sealed class TrackerAdminResult
{
    public string Message { get; set; } = "";
    public string Priority { get; set; } = "info";
    public int DurationSeconds { get; set; } = 15;
    public long Timestamp { get; set; }
    public string MessageId { get; set; } = "";
}

internal sealed class TrackerPeerDashboard
{
    public string PeerId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Address { get; set; } = "";
    public string WorldId { get; set; } = "";
    public int PlayerCount { get; set; }
    public DateTime LastSeen { get; set; }
}

internal sealed class AdminBroadcastResult
{
    public string MessageId { get; set; } = "";
    public string Message { get; set; } = "";
}

// --- Overworld API typed results ---
internal sealed class OverworldPlayerResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public float X { get; set; }
    public float Y { get; set; }
    public string Status { get; set; } = "";
    public string PartyId { get; set; } = "";
}

internal sealed class OverworldPartyResult
{
    public string Id { get; set; } = "";
    public string LeaderId { get; set; } = "";
    public string LeaderName { get; set; } = "";
    public int MemberCount { get; set; }
    public OverworldPartyMemberResult[] Members { get; set; } = Array.Empty<OverworldPartyMemberResult>();
    public string Status { get; set; } = "";
}

internal sealed class OverworldPartyMemberResult
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

internal sealed class OverworldStatsResult
{
    public int TotalConnected { get; set; }
    public int InOverworld { get; set; }
    public int InDungeon { get; set; }
    public int InParties { get; set; }
    public int TotalParties { get; set; }
}

internal sealed class AdminMessageResult
{
    public string MessageId { get; set; } = "";
    public string Message { get; set; } = "";
    public string Priority { get; set; } = "";
    public int DurationSeconds { get; set; }
    public long Timestamp { get; set; }
}

internal sealed class P2PErrorResult
{
    public string Error { get; set; } = "";
}

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
[JsonSerializable(typeof(OverworldMap))]
[JsonSerializable(typeof(Landmark))]
[JsonSerializable(typeof(List<Landmark>))]
[JsonSerializable(typeof(DungeonEntrance))]
[JsonSerializable(typeof(List<DungeonEntrance>))]
[JsonSerializable(typeof(WorldObject))]
[JsonSerializable(typeof(List<WorldObject>))]
[JsonSerializable(typeof(SpawnPoint))]
[JsonSerializable(typeof(SimpleMessageResult))]
[JsonSerializable(typeof(TrackerReflectResult))]
[JsonSerializable(typeof(TrackerRegisterResult))]
[JsonSerializable(typeof(TrackerPeerResult))]
[JsonSerializable(typeof(TrackerPeerResult[]))]
[JsonSerializable(typeof(TrackerAdminResult))]
[JsonSerializable(typeof(TrackerAdminResult[]))]
[JsonSerializable(typeof(TrackerPeerDashboard))]
[JsonSerializable(typeof(TrackerPeerDashboard[]))]
[JsonSerializable(typeof(AdminBroadcastResult))]
[JsonSerializable(typeof(OverworldPlayerResult))]
[JsonSerializable(typeof(OverworldPlayerResult[]))]
[JsonSerializable(typeof(OverworldPartyResult))]
[JsonSerializable(typeof(OverworldPartyResult[]))]
[JsonSerializable(typeof(OverworldPartyMemberResult))]
[JsonSerializable(typeof(OverworldPartyMemberResult[]))]
[JsonSerializable(typeof(OverworldStatsResult))]
[JsonSerializable(typeof(AdminMessageResult))]
[JsonSerializable(typeof(AdminMessageResult[]))]
[JsonSerializable(typeof(P2PErrorResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class MatchmakingJsonContext : JsonSerializerContext { }

/// <summary>
/// AOT JSON context for tracker request deserialization (case-insensitive).
/// </summary>
[JsonSerializable(typeof(TrackerPeerEntry))]
[JsonSerializable(typeof(AdminBroadcastRequest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
internal partial class MatchmakingTrackerJsonContext : JsonSerializerContext { }
