using System.Collections.Concurrent;

namespace Carcosa.Server.Game;

/// <summary>
/// The complete authoritative game state maintained by the server.
/// </summary>
public sealed class GameState
{
    /// <summary>Current server tick number (increments each game loop iteration).</summary>
    public int Tick { get; set; }

    /// <summary>All entities in the game world (players, enemies, projectiles).</summary>
    public ConcurrentDictionary<string, Entity> Entities { get; } = new();

    /// <summary>The tile map for collision detection. Null until map is generated.</summary>
    public TileMap? Map { get; set; }

    /// <summary>Current game phase.</summary>
    public GamePhase Phase { get; set; } = GamePhase.Lobby;

    /// <summary>Current wave number (1-indexed).</summary>
    public int CurrentWave { get; set; }

    /// <summary>Ticks remaining until next wave starts.</summary>
    public int WaveCountdownTicks { get; set; }

    /// <summary>Number of enemies remaining in current wave.</summary>
    public int EnemiesRemaining { get; set; }

    /// <summary>
    /// Add an entity to the game state.
    /// </summary>
    public void AddEntity(Entity entity)
    {
        Entities[entity.Id] = entity;
    }

    /// <summary>
    /// Remove an entity from the game state.
    /// </summary>
    public bool RemoveEntity(string id)
    {
        return Entities.TryRemove(id, out _);
    }

    /// <summary>
    /// Get a player entity by owner (player connection) ID.
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
    /// </summary>
    public void ClearDirtyFlags()
    {
        foreach (var (_, entity) in Entities)
        {
            entity.IsDirty = false;
        }
    }

    /// <summary>
    /// Get all living player entities.
    /// </summary>
    public IEnumerable<Entity> GetAlivePlayers()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Player && entity.IsAlive)
                yield return entity;
        }
    }

    /// <summary>
    /// Get all living enemy entities.
    /// </summary>
    public IEnumerable<Entity> GetAliveEnemies()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Enemy && entity.IsAlive)
                yield return entity;
        }
    }

    /// <summary>
    /// Get all active projectile entities.
    /// </summary>
    public IEnumerable<Entity> GetProjectiles()
    {
        foreach (var (_, entity) in Entities)
        {
            if (entity.Type == EntityType.Projectile && entity.IsAlive)
                yield return entity;
        }
    }
}

public enum GamePhase
{
    Lobby,
    Playing,
    WaveIntermission,
    GameOver,
    Victory
}

/// <summary>
/// The tile-based map of the game world. Generated via BSP algorithm.
/// </summary>
public sealed class TileMap
{
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[] Tiles { get; init; } = [];
    public int Seed { get; init; }
    public Room[] Rooms { get; init; } = [];
    public SpawnPoint[] SpawnPoints { get; init; } = [];

    /// <summary>
    /// Get the tile type at a position.
    /// </summary>
    public TileType GetTile(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return TileType.Wall;
        return (TileType)Tiles[y * Width + x];
    }

    /// <summary>
    /// Check if a position is walkable (not wall or water).
    /// </summary>
    public bool IsWalkable(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            return false;

        var tile = (TileType)Tiles[y * Width + x];
        return tile != TileType.Wall && tile != TileType.Water;
    }

    /// <summary>
    /// Check if a floating-point position is walkable.
    /// Uses a small entity radius to prevent clipping into walls.
    /// </summary>
    public bool IsWalkableF(float x, float y, float radius = 0.3f)
    {
        // Check all four corners of the entity bounding box
        return IsWalkable((int)(x - radius), (int)(y - radius))
            && IsWalkable((int)(x + radius), (int)(y - radius))
            && IsWalkable((int)(x - radius), (int)(y + radius))
            && IsWalkable((int)(x + radius), (int)(y + radius));
    }

    /// <summary>
    /// Find a valid spawn position for a player (on a floor or cobblestone tile).
    /// </summary>
    public (float X, float Y) FindPlayerSpawn(Random rng)
    {
        // Try rooms first
        if (Rooms.Length > 0)
        {
            var room = Rooms[rng.Next(Rooms.Length)];
            return (room.Center.X + 0.5f, room.Center.Y + 0.5f);
        }

        // Fallback: scan for walkable tile
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var x = rng.Next(5, Width - 5);
            var y = rng.Next(5, Height - 5);
            if (IsWalkable(x, y))
                return (x + 0.5f, y + 0.5f);
        }

        return (Width / 2f, Height / 2f);
    }

    /// <summary>
    /// Serialize the map to a byte array for network transmission.
    /// Format: [width:4][height:4][seed:4][tiles:width*height]
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
    /// </summary>
    public string ToBase64() => Convert.ToBase64String(Tiles);
}
