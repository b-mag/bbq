// =============================================================================
// GameState.cs — Server-Authoritative Game World State
// =============================================================================
//
// WHY CENTRALIZED STATE:
// The server is the single source of truth for all game state. This prevents
// cheating (clients can't modify their own health/position) and ensures all
// players see a consistent world. Clients predict locally for responsiveness
// but always defer to server corrections.
//
// WHY ConcurrentDictionary FOR ENTITIES:
// Entities are accessed from the game loop thread (every tick for updates) AND
// from WebSocket threads (when a player connects/disconnects and their entity
// needs to be added/removed). ConcurrentDictionary provides lock-free reads
// and safe concurrent writes without manual locking.
//
// WHY TILE MAP:
// The tile map is generated once when the game starts and never modified during
// gameplay. It's used for collision detection (every movement tick) and pathfinding
// (every AI tick). Storing it as a flat byte array with index math (y * width + x)
// gives cache-friendly access patterns for the hot collision check path.
// =============================================================================

using System.Collections.Concurrent;

namespace Carcosa.Server.Game;

/// <summary>
/// The complete authoritative game state maintained by the server.
/// All game systems read from and write to this shared state object.
/// The game loop ensures systems run sequentially within a tick, so
/// concurrent access is only an issue between the game loop thread
/// and the WebSocket/HTTP threads.
/// </summary>
public sealed class GameState
{
    /// <summary>
    /// Current server tick number (increments each game loop iteration).
    /// Used by clients for interpolation timing and input reconciliation.
    /// At 20Hz, this overflows int.MaxValue after ~3.4 years of continuous play.
    /// </summary>
    public int Tick { get; set; }

    /// <summary>
    /// All entities in the game world (players, enemies, projectiles).
    /// Keyed by entity ID for O(1) lookup. Concurrent for cross-thread safety.
    /// </summary>
    public ConcurrentDictionary<string, Entity> Entities { get; } = new();

    /// <summary>
    /// The tile map for collision detection and pathfinding.
    /// Null until the host starts the game and map generation completes.
    /// Immutable once set (never modified during gameplay).
    /// </summary>
    public TileMap? Map { get; set; }

    /// <summary>Current game phase (controls which systems are active).</summary>
    public GamePhase Phase { get; set; } = GamePhase.Lobby;

    /// <summary>Current wave number (1-indexed). Zero means game hasn't started.</summary>
    public int CurrentWave { get; set; }

    /// <summary>Which map scenario is being played. Affects map generation and wave rules.</summary>
    public MapScenario Scenario { get; set; } = MapScenario.DrownedDock;

    /// <summary>
    /// Average party level this instance scaled to. Drives enemy HP/damage,
    /// XP/loot, auto-aggro (off at 10 and below), and enemy projectiles (off at 7 and below).
    /// </summary>
    public int AvgLevel { get; set; } = 1;

    /// <summary>Ticks remaining until next wave starts (during intermission).</summary>
    public int WaveCountdownTicks { get; set; }

    /// <summary>Number of enemies remaining in current wave (cached for performance).</summary>
    public int EnemiesRemaining { get; set; }

    /// <summary>
    /// Add an entity to the game state. Overwrites if ID already exists.
    /// </summary>
    public void AddEntity(Entity entity)
    {
        Entities[entity.Id] = entity;
    }

    /// <summary>
    /// Remove an entity from the game state. Returns true if it was present.
    /// </summary>
    public bool RemoveEntity(string id)
    {
        return Entities.TryRemove(id, out _);
    }

    /// <summary>
    /// Find a player entity by the owning connection ID.
    /// Used to map incoming inputs to the correct entity.
    /// 
    /// WHY LINEAR SCAN: With max 8 players, a dictionary lookup by OwnerId
    /// would save microseconds but add memory overhead. Linear scan over 8
    /// items is effectively instant.
    /// </summary>
    public Entity? GetPlayerByOwnerId(string ownerId)
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Player && entity.OwnerId == ownerId)
                return entity;
        }
        return null;
    }

    /// <summary>
    /// Get all entities that have been modified since last broadcast (dirty flag set).
    /// Used by the game loop to build delta state updates for clients.
    /// 
    /// WHY YIELD: Avoids allocating a list when we just need to iterate once.
    /// The caller (BroadcastState) converts to array for serialization.
    /// </summary>
    public IEnumerable<Entity> GetDirtyEntities()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.IsDirty)
                yield return entity;
        }
    }

    /// <summary>
    /// Clear all dirty flags after broadcasting state.
    /// Called at the end of each tick after BroadcastState completes.
    /// </summary>
    public void ClearDirtyFlags()
    {
        foreach (var (_, entity) in Entities)
        {
            entity.IsDirty = false;
        }
    }

    /// <summary>Get all living player entities (for game-over detection and healing).</summary>
    public IEnumerable<Entity> GetAlivePlayers()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Player && entity.IsAlive)
                yield return entity;
        }
    }

    /// <summary>Get all living enemy entities (for wave completion detection).</summary>
    public IEnumerable<Entity> GetAliveEnemies()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Enemy && entity.IsAlive)
                yield return entity;
        }
    }

    /// <summary>Get all active projectile entities (for movement and collision updates).</summary>
    public IEnumerable<Entity> GetProjectiles()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Projectile && entity.IsAlive)
                yield return entity;
        }
    }
}

/// <summary>
/// Game phase determines which systems are active and what the client displays.
/// Transitions: Lobby → Playing ↔ WaveIntermission → GameOver/Victory → (Lobby)
/// </summary>
public enum GamePhase
{
    /// <summary>Players are in the lobby selecting classes and readying up.</summary>
    Lobby,
    /// <summary>Active gameplay — entities move, combat is live, waves spawn.</summary>
    Playing,
    /// <summary>Brief pause between waves for players to regroup.</summary>
    WaveIntermission,
    /// <summary>All players defeated — game is over.</summary>
    GameOver,
    /// <summary>Boss defeated — players win.</summary>
    Victory
}

/// <summary>
/// Map scenario selection. Determines map layout, wave rules, and victory conditions.
/// Names follow the Carcosa dark fantasy theme.
/// </summary>
public enum MapScenario
{
    /// <summary>
    /// The Drowned Dock — BSP-generated rooms/corridors, 5 waves + boss.
    /// A waterlogged fishing village dungeon. Standard co-op with a clear win condition.
    /// </summary>
    DrownedDock,
    /// <summary>
    /// The Pallid Sanctum — Large open arena, endless escalating waves.
    /// King in Yellow vibes. Survival until all players fall.
    /// Pale Marks awarded per wave survived (10 per wave).
    /// (Formerly "The Temple")
    /// </summary>
    PallidSanctum,
    /// <summary>
    /// The Hollow — Generic cave dungeon. BSP-generated, 3 waves + mini-boss.
    /// Shorter dungeon for quick runs.
    /// </summary>
    Hollow,
    /// <summary>
    /// Mountain Cave — Cellular-automata / drunkard-walk cave, ~60x50.
    /// Mesh-native dungeon instance entered from the overworld.
    /// </summary>
    MountainCave
}

// =============================================================================
// TileMap — Binary Grid Map for Collision and Rendering
// =============================================================================

/// <summary>
/// The tile-based map of the game world. Generated via BSP algorithm at game start.
/// 
/// WHY FLAT BYTE ARRAY: A 2D array (byte[,]) has bounds checking overhead on every
/// access. A flat byte[] with manual index math (y * Width + x) is faster and more
/// cache-friendly for row-major traversal (which is how we iterate for rendering
/// and collision). The map is typically 80x60 = 4800 bytes — fits in L1 cache.
/// 
/// IMMUTABILITY: Once generated, the map never changes during gameplay. This means
/// no synchronization is needed between the game loop thread and other threads that
/// might read the map (e.g., for the /api/map endpoint).
/// </summary>
public sealed class TileMap
{
    public int Width { get; init; }
    public int Height { get; init; }
    /// <summary>
    /// Flat array of tile types. Access pattern: tiles[y * Width + x].
    /// Each byte is cast to/from the TileType enum.
    /// </summary>
    public byte[] Tiles { get; init; } = [];
    /// <summary>Random seed used for generation (allows reproducibility for debugging).</summary>
    public int Seed { get; init; }
    /// <summary>Rooms identified during generation (used for spawn point selection).</summary>
    public Room[] Rooms { get; init; } = [];
    /// <summary>Pre-identified spawn points for enemies and players.</summary>
    public SpawnPoint[] SpawnPoints { get; init; } = [];

    /// <summary>
    /// Get the tile type at an integer position. Returns Wall for out-of-bounds
    /// (treats the map edge as an impassable border).
    /// </summary>
    public TileType GetTile(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return TileType.Wall;
        return (TileType)Tiles[y * Width + x];
    }

    /// <summary>
    /// Check if an integer tile position is walkable (not wall or water).
    /// This is the hot-path collision check called hundreds of times per tick.
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;

        var tile = (TileType)Tiles[y * Width + x];
        return tile != TileType.Wall && tile != TileType.Water;
    }

    /// <summary>
    /// Check if a floating-point position is walkable using an entity bounding box.
    /// 
    /// WHY 4-CORNER CHECK: Entities have a radius (default 0.3 tiles). To prevent
    /// them from clipping into walls, we check all four corners of their bounding
    /// box. If any corner is in a wall tile, the position is blocked.
    /// This is much cheaper than actual circle-vs-grid collision.
    /// </summary>
    public bool IsWalkableF(float x, float y, float radius = 0.3f)
    {
        return IsWalkable((int)(x - radius), (int)(y - radius))
            && IsWalkable((int)(x + radius), (int)(y - radius))
            && IsWalkable((int)(x - radius), (int)(y + radius))
            && IsWalkable((int)(x + radius), (int)(y + radius));
    }

    /// <summary>
    /// Find a valid spawn position for a player entity.
    /// Prefers room centers (indoor spawn points) for safety.
    /// Falls back to random walkable tiles if no rooms exist.
    /// </summary>
    public (float X, float Y) FindPlayerSpawn(Random rng)
    {
        foreach (var sp in SpawnPoints)
        {
            if (sp.Type == SpawnPointType.Player && IsWalkable(sp.X, sp.Y))
                return (sp.X + 0.5f, sp.Y + 0.5f);
        }

        // Try rooms first — spawning in a room center is safe and grouped
        if (Rooms.Length > 0)
        {
            var room = Rooms[rng.Next(Rooms.Length)];
            return (room.Center.X + 0.5f, room.Center.Y + 0.5f);
        }

        // Fallback: scan for any walkable tile
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var x = rng.Next(5, Width - 5);
            var y = rng.Next(5, Height - 5);
            if (IsWalkable(x, y))
                return (x + 0.5f, y + 0.5f);
        }

        // Last resort: center of map (should never happen with valid generation)
        return (Width / 2f, Height / 2f);
    }

    /// <summary>
    /// Serialize the map to a binary byte array for efficient network transmission.
    /// Format: [width:4 bytes LE][height:4 bytes LE][seed:4 bytes LE][tiles:W*H bytes]
    /// </summary>
    public byte[] Serialize()
    {
        var data = new byte[12 + Tiles.Length];
        BitConverter.GetBytes(Width).CopyTo(data, 0);
        BitConverter.GetBytes(Height).CopyTo(data, 4);
        BitConverter.GetBytes(Seed).CopyTo(data, 8);
        Tiles.CopyTo(data, 12);
        return data;
    }

    /// <summary>
    /// Convert tiles to a base64 string for JSON transmission.
    /// Used by the MapData message to send the entire map to clients on game start.
    /// </summary>
    public string ToBase64() => Convert.ToBase64String(Tiles);
}
