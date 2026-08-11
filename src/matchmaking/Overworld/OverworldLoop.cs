// =============================================================================
// OverworldLoop.cs — Lightweight Game Loop for the Persistent Overworld
// =============================================================================
//
// A 20 tick/sec loop that processes player movement in the shared overworld.
// Much simpler than the dungeon GameLoop — no combat, no AI, no waves.
// Just movement, collision, and state broadcasting.
//
// Players that are "in_dungeon" are skipped (their state is managed by the
// dungeon instance). They reappear when they return.
// =============================================================================

using System.Collections.Concurrent;

namespace Carcosa.Matchmaking.Overworld;

/// <summary>
/// Represents a player's state in the overworld.
/// </summary>
public sealed class OverworldPlayer
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public float X { get; set; }
    public float Y { get; set; }
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }
    public string Status { get; set; } = "exploring"; // exploring, in_party, in_dungeon
    public string? PartyId { get; set; }
    public bool IsPartyLeader { get; set; }
    public bool IsDirty { get; set; } = true; // Needs broadcast
    public int LastProcessedInput { get; set; }

    // Input queue for this player
    public ConcurrentQueue<OwPlayerInputPayload> InputQueue { get; } = new();
}

/// <summary>
/// The overworld game loop. Runs at 20 ticks/sec, processes movement,
/// and broadcasts state to all connected players.
/// </summary>
public sealed class OverworldLoop
{
    private const float TickRate = 20f;
    private const float TickInterval = 1000f / TickRate; // 50ms
    private const float PlayerSpeed = 4.5f; // tiles/second
    private const float MovePerTick = PlayerSpeed / TickRate;

    private readonly OverworldConnectionManager _connections;
    private readonly OverworldMapStore _mapStore;
    private readonly ConcurrentDictionary<string, OverworldPlayer> _players = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;
    private int _tick;

    // Cached map data for collision
    private byte[]? _tiles;
    private int _mapWidth;
    private int _mapHeight;
    private List<WorldObject> _collisionObjects = new();

    public IReadOnlyDictionary<string, OverworldPlayer> Players => _players;
    public int Tick => _tick;

    public OverworldLoop(OverworldConnectionManager connections, OverworldMapStore mapStore)
    {
        _connections = connections;
        _mapStore = mapStore;
    }

    /// <summary>Start the game loop on a background thread.</summary>
    public void Start()
    {
        // Cache map tiles for collision checking
        var map = _mapStore.GetMap();
        _tiles = map.DecodeTiles();
        _mapWidth = map.Width;
        _mapHeight = map.Height;

        // Cache collision objects for proximity checks
        _collisionObjects = map.WorldObjects.Where(o => o.Collision).ToList();

        _loopTask = Task.Run(() => RunLoop(_cts.Token));
        Console.WriteLine("[Overworld] Game loop started (20 tick/sec)");
    }

    /// <summary>Stop the game loop.</summary>
    public void Stop()
    {
        _cts.Cancel();
        _loopTask?.Wait(TimeSpan.FromSeconds(2));
        Console.WriteLine("[Overworld] Game loop stopped");
    }

    /// <summary>Add a player to the overworld at the spawn point.</summary>
    public OverworldPlayer AddPlayer(string playerId, string playerName)
    {
        var map = _mapStore.GetMap();
        var player = new OverworldPlayer
        {
            Id = playerId,
            Name = playerName,
            X = map.SpawnPoint.X + 0.5f,
            Y = map.SpawnPoint.Y + 0.5f,
        };
        _players[playerId] = player;
        return player;
    }

    /// <summary>Remove a player from the overworld.</summary>
    public void RemovePlayer(string playerId)
    {
        _players.TryRemove(playerId, out _);
    }

    /// <summary>Queue movement input for a player.</summary>
    public void QueueInput(string playerId, OwPlayerInputPayload input)
    {
        if (_players.TryGetValue(playerId, out var player))
        {
            player.InputQueue.Enqueue(input);
        }
    }

    /// <summary>Get a player's state.</summary>
    public OverworldPlayer? GetPlayer(string playerId)
    {
        _players.TryGetValue(playerId, out var player);
        return player;
    }

    private async Task RunLoop(CancellationToken ct)
    {
        var nextTick = Environment.TickCount64;

        while (!ct.IsCancellationRequested)
        {
            var now = Environment.TickCount64;
            if (now < nextTick)
            {
                var sleepMs = (int)(nextTick - now);
                if (sleepMs > 0)
                    await Task.Delay(sleepMs, ct).ConfigureAwait(false);
                continue;
            }

            nextTick += (long)TickInterval;
            _tick++;

            // Process all player inputs
            ProcessInputs();

            // Broadcast state (dirty players only)
            await BroadcastState(ct);
        }
    }

    private void ProcessInputs()
    {
        foreach (var (_, player) in _players)
        {
            // Skip players in dungeons
            if (player.Status == "in_dungeon") continue;

            // Process queued inputs (take latest only for this tick)
            OwPlayerInputPayload? latestInput = null;
            while (player.InputQueue.TryDequeue(out var input))
            {
                latestInput = input;
            }

            if (latestInput != null)
            {
                ApplyMovement(player, latestInput);
                player.LastProcessedInput = latestInput.SequenceNumber;
            }
            else
            {
                // No input — player is stationary
                if (player.VelocityX != 0 || player.VelocityY != 0)
                {
                    player.VelocityX = 0;
                    player.VelocityY = 0;
                    player.IsDirty = true;
                }
            }
        }
    }

    private void ApplyMovement(OverworldPlayer player, OwPlayerInputPayload input)
    {
        var moveX = input.MoveX;
        var moveY = input.MoveY;

        // Normalize diagonal movement
        if (moveX != 0 && moveY != 0)
        {
            var len = MathF.Sqrt(moveX * moveX + moveY * moveY);
            moveX /= len;
            moveY /= len;
        }

        var dx = moveX * MovePerTick;
        var dy = moveY * MovePerTick;

        if (dx == 0 && dy == 0)
        {
            if (player.VelocityX != 0 || player.VelocityY != 0)
            {
                player.VelocityX = 0;
                player.VelocityY = 0;
                player.IsDirty = true;
            }
            return;
        }

        // Try X movement
        var newX = player.X + dx;
        if (IsWalkable(newX, player.Y))
        {
            player.X = newX;
        }

        // Try Y movement
        var newY = player.Y + dy;
        if (IsWalkable(player.X, newY))
        {
            player.Y = newY;
        }

        player.VelocityX = dx;
        player.VelocityY = dy;
        player.IsDirty = true;
    }

    /// <summary>
    /// Check if a position is walkable using the same bounding-box pattern as the dungeon.
    /// Also checks collision against world objects.
    /// </summary>
    private bool IsWalkable(float x, float y)
    {
        if (_tiles == null) return true;

        const float radius = 0.3f;
        // Check 4 corners of the bounding box against tiles
        if (!IsPointWalkable(x - radius, y - radius) ||
            !IsPointWalkable(x + radius, y - radius) ||
            !IsPointWalkable(x - radius, y + radius) ||
            !IsPointWalkable(x + radius, y + radius))
        {
            return false;
        }

        // Check collision against world objects
        foreach (var obj in _collisionObjects)
        {
            var dx = x - obj.X;
            var dy = y - obj.Y;
            var combinedRadius = radius + obj.CollisionRadius;
            if (dx * dx + dy * dy < combinedRadius * combinedRadius)
            {
                return false;
            }
        }

        return true;
    }

    private bool IsPointWalkable(float x, float y)
    {
        var tileX = (int)x;
        var tileY = (int)y;

        if (tileX < 0 || tileX >= _mapWidth || tileY < 0 || tileY >= _mapHeight)
            return false;

        var tile = (OverworldTileType)_tiles![tileY * _mapWidth + tileX];
        return OverworldMap.IsWalkable(tile);
    }

    /// <summary>
    /// Broadcast the world state to all connected players.
    /// Only includes dirty (moved) players for delta compression.
    /// </summary>
    private async Task BroadcastState(CancellationToken ct)
    {
        // Gather dirty players
        var dirtyPlayers = new List<OwPlayerState>();
        var dirtyPlayerIds = new HashSet<string>();
        foreach (var (_, player) in _players)
        {
            if (!player.IsDirty) continue;
            if (player.Status == "in_dungeon") continue;

            dirtyPlayers.Add(new OwPlayerState
            {
                Id = player.Id,
                Name = player.Name,
                X = player.X,
                Y = player.Y,
                VelocityX = player.VelocityX,
                VelocityY = player.VelocityY,
                Status = player.Status,
                PartyId = player.PartyId,
                IsPartyLeader = player.IsPartyLeader,
            });
            dirtyPlayerIds.Add(player.Id);

            player.IsDirty = false;
        }

        if (dirtyPlayers.Count == 0) return;

        // Send personalized messages (each player gets their own LastProcessedInput)
        // Only include LastProcessedInput if the recipient's own state was updated
        var playersArray = dirtyPlayers.ToArray();
        foreach (var (playerId, _) in _players)
        {
            if (!_connections.GetConnectedPlayerIds().Contains(playerId)) continue;

            var player = _players.GetValueOrDefault(playerId);
            var msg = new OverworldMessage
            {
                Type = OwMessageTypes.WorldState,
                WorldState = new OwWorldStatePayload
                {
                    Tick = _tick,
                    Players = playersArray,
                    // Only send LastProcessedInput when this player's own state is in the update
                    LastProcessedInput = dirtyPlayerIds.Contains(playerId) ? player?.LastProcessedInput : null,
                }
            };

            await _connections.SendToAsync(playerId, msg, ct);
        }
    }
}
