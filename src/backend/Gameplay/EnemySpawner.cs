// =============================================================================
// EnemySpawner.cs — Overworld Enemy Population Manager
// =============================================================================
//
// OVERVIEW:
// Manages spawning, respawning, and lifecycle of overworld enemies (Gronks).
// Only runs on the SHARD HOST — other peers receive enemy state via P2P sync.
//
// ZONE-BASED SPAWNING:
// Enemies spawn within predefined rectangular zones on the overworld map.
// Zones represent grassy/open areas where Gronks naturally congregate.
// Each zone has a maximum enemy count; killed enemies respawn in the same
// zone after a cooldown period.
//
// WHY ZONE-BASED (not fixed spawn points):
// - Feels more organic — enemies appear in different positions each time
// - Prevents "camping" specific spawn points
// - Scales easily — adding more zones or adjusting counts is trivial
// - Zones can be themed (different enemy types per zone in future)
//
// RESPAWN TIMER:
// Dead enemies respawn after 60 seconds (1200 ticks). This gives players time
// to loot and move on, but ensures the world always feels populated.
//
// HOST MIGRATION:
// When host changes, the new host spawns a fresh set of enemies. No state
// transfer needed — Gronks are ambient creatures, not quest-critical.
// =============================================================================

using System.Collections.Concurrent;
using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Defines a rectangular spawn zone where enemies can appear.
/// Zones are positioned over grassy/open terrain on the overworld map.
/// </summary>
public sealed record SpawnZone
{
    /// <summary>Unique identifier for this zone (for logging/debugging).</summary>
    public required string Id { get; init; }

    /// <summary>Left boundary (tile X coordinate).</summary>
    public required int MinX { get; init; }

    /// <summary>Right boundary (tile X coordinate).</summary>
    public required int MaxX { get; init; }

    /// <summary>Top boundary (tile Y coordinate).</summary>
    public required int MinY { get; init; }

    /// <summary>Bottom boundary (tile Y coordinate).</summary>
    public required int MaxY { get; init; }

    /// <summary>Maximum number of enemies alive in this zone simultaneously.</summary>
    public required int MaxEnemies { get; init; }

    /// <summary>Enemy SubType to spawn in this zone (e.g., "gronk").</summary>
    public string EnemyType { get; init; } = "gronk";
}

/// <summary>
/// Tracks a dead enemy's respawn timer.
/// </summary>
internal sealed class RespawnEntry
{
    public required SpawnZone Zone { get; init; }
    public int TicksRemaining { get; set; }
}

/// <summary>
/// Manages enemy spawning and respawning for the overworld.
/// Only active on the shard host — spawns enemies when becoming host,
/// despawns all when losing host status.
/// </summary>
public sealed class EnemySpawner
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>
    /// Ticks before a dead enemy respawns. 1200 ticks = 60 seconds at 20Hz.
    /// Long enough for loot collection, short enough to keep the world populated.
    /// </summary>
    private const int RespawnDelayTicks = 1200;

    /// <summary>
    /// Ticks before a dead enemy corpse is removed from state. 600 ticks = 30 seconds.
    /// Gives visual feedback that something died here recently.
    /// </summary>
    private const int CorpseLingerTicks = 600;

    // =========================================================================
    // FIELDS
    // =========================================================================

    private readonly ConcurrentDictionary<string, Entity> _enemies = new();
    private readonly List<RespawnEntry> _respawnQueue = new();
    private readonly object _respawnLock = new();
    private int _enemyCounter;
    private bool _active;

    // =========================================================================
    // SPAWN ZONE DEFINITIONS
    // =========================================================================
    // These zones are hardcoded based on the known overworld map layout.
    // The Dim Shore overworld has grassy areas south/central where Gronks roam.
    // Coordinates are approximate — zones should overlap with grass tiles.

    private static readonly SpawnZone[] DefaultZones =
    [
        new SpawnZone
        {
            Id = "south_meadow",
            MinX = 80, MaxX = 120,
            MinY = 160, MaxY = 200,
            MaxEnemies = 5,
            EnemyType = "gronk",
        },
        new SpawnZone
        {
            Id = "central_plains",
            MinX = 90, MaxX = 130,
            MinY = 130, MaxY = 160,
            MaxEnemies = 4,
            EnemyType = "gronk",
        },
        new SpawnZone
        {
            Id = "eastern_grove",
            MinX = 130, MaxX = 160,
            MinY = 150, MaxY = 185,
            MaxEnemies = 3,
            EnemyType = "gronk",
        },
    ];

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>All currently tracked enemies (alive and dead corpses).</summary>
    public IReadOnlyDictionary<string, Entity> Enemies => _enemies;

    /// <summary>Whether the spawner is actively managing enemies (host only).</summary>
    public bool IsActive => _active;

    /// <summary>Count of currently alive enemies.</summary>
    public int AliveCount => _enemies.Values.Count(e => e.IsAlive);

    // =========================================================================
    // LIFECYCLE
    // =========================================================================

    /// <summary>
    /// Activate the spawner (called when this peer becomes shard host).
    /// Spawns initial enemies in all zones.
    /// </summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;

        Console.WriteLine("[EnemySpawner] Activating — spawning initial Gronk population...");

        foreach (var zone in DefaultZones)
        {
            for (int i = 0; i < zone.MaxEnemies; i++)
            {
                SpawnEnemy(zone);
            }
        }

        Console.WriteLine($"[EnemySpawner] Spawned {_enemies.Count} Gronks across {DefaultZones.Length} zones.");
    }

    /// <summary>
    /// Deactivate the spawner (called when this peer loses host status).
    /// Removes all enemies — the new host will spawn their own.
    /// </summary>
    public void Deactivate()
    {
        if (!_active) return;
        _active = false;

        _enemies.Clear();
        lock (_respawnLock)
        {
            _respawnQueue.Clear();
        }

        Console.WriteLine("[EnemySpawner] Deactivated — cleared all enemies.");
    }

    // =========================================================================
    // SPAWNING
    // =========================================================================

    /// <summary>
    /// Spawn a single enemy in the given zone at a random walkable position.
    /// </summary>
    private Entity SpawnEnemy(SpawnZone zone)
    {
        var rng = Random.Shared;
        var id = $"enemy_{Interlocked.Increment(ref _enemyCounter)}";

        // Random position within zone bounds
        float x = rng.Next(zone.MinX, zone.MaxX) + rng.NextSingle();
        float y = rng.Next(zone.MinY, zone.MaxY) + rng.NextSingle();

        var enemy = new Entity
        {
            Id = id,
            Type = EntityType.Enemy,
            SubType = zone.EnemyType,
            X = x,
            Y = y,
            SpawnX = x,
            SpawnY = y,
            Health = 30,
            MaxHealth = 30,
            Speed = 1.5f,       // Slow wander speed (tiles/sec)
            Damage = 5,         // Peck attack damage
            IsAlive = true,
            IsDirty = true,
            // Initialize wander target to spawn position (will be randomized on first AI tick)
            WanderTargetX = x,
            WanderTargetY = y,
        };

        _enemies[id] = enemy;
        return enemy;
    }

    // =========================================================================
    // TICK PROCESSING
    // =========================================================================

    /// <summary>
    /// Process one tick of enemy spawner logic. Handles:
    ///   1. Respawn timer countdown
    ///   2. Corpse removal after linger period
    ///   3. Spawning new enemies when respawn timer expires
    /// 
    /// Called every tick (20Hz) by the combat sync loop on the shard host.
    /// </summary>
    public void ProcessTick()
    {
        if (!_active) return;

        // Process respawn timers
        lock (_respawnLock)
        {
            for (int i = _respawnQueue.Count - 1; i >= 0; i--)
            {
                _respawnQueue[i].TicksRemaining--;

                if (_respawnQueue[i].TicksRemaining <= 0)
                {
                    // Respawn in the same zone
                    var zone = _respawnQueue[i].Zone;
                    int aliveInZone = CountAliveInZone(zone);

                    if (aliveInZone < zone.MaxEnemies)
                    {
                        SpawnEnemy(zone);
                    }

                    _respawnQueue.RemoveAt(i);
                }
            }
        }

        // Remove lingering corpses (dead entities past their linger time)
        var toRemove = new List<string>();
        foreach (var (id, enemy) in _enemies)
        {
            if (!enemy.IsAlive)
            {
                // Use AggroTicks as a death timer (repurposed after death)
                enemy.AggroTicks++;
                if (enemy.AggroTicks >= CorpseLingerTicks)
                {
                    toRemove.Add(id);
                }
            }
        }

        foreach (var id in toRemove)
        {
            _enemies.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Notify the spawner that an enemy has died. Starts the respawn timer
    /// for the zone that enemy belonged to.
    /// </summary>
    /// <param name="enemyId">The ID of the dead enemy.</param>
    public void NotifyEnemyDeath(string enemyId)
    {
        if (!_enemies.TryGetValue(enemyId, out var enemy)) return;
        if (enemy.IsAlive) return; // Not actually dead

        // Find which zone this enemy belonged to (by spawn position)
        var zone = FindZoneForPosition(enemy.SpawnX, enemy.SpawnY);
        if (zone == null) return;

        // Reset aggro ticks for use as corpse timer
        enemy.AggroTicks = 0;

        // Queue respawn
        lock (_respawnLock)
        {
            _respawnQueue.Add(new RespawnEntry
            {
                Zone = zone,
                TicksRemaining = RespawnDelayTicks,
            });
        }

        Console.WriteLine($"[EnemySpawner] Gronk {enemyId} died in zone '{zone.Id}'. Respawning in {RespawnDelayTicks / 20}s.");
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>Count alive enemies whose spawn position is within a zone.</summary>
    private int CountAliveInZone(SpawnZone zone)
    {
        int count = 0;
        foreach (var (_, enemy) in _enemies)
        {
            if (enemy.IsAlive
                && enemy.SpawnX >= zone.MinX && enemy.SpawnX <= zone.MaxX
                && enemy.SpawnY >= zone.MinY && enemy.SpawnY <= zone.MaxY)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>Find which zone a position belongs to (for respawn routing).</summary>
    private static SpawnZone? FindZoneForPosition(float x, float y)
    {
        foreach (var zone in DefaultZones)
        {
            if (x >= zone.MinX && x <= zone.MaxX && y >= zone.MinY && y <= zone.MaxY)
            {
                return zone;
            }
        }
        return DefaultZones.Length > 0 ? DefaultZones[0] : null; // Fallback to first zone
    }

    /// <summary>
    /// Get all alive enemies as a list (for sync broadcasting).
    /// </summary>
    public List<Entity> GetAliveEnemies()
    {
        return _enemies.Values.Where(e => e.IsAlive).ToList();
    }

    /// <summary>
    /// Get all enemies including corpses (for frontend rendering).
    /// </summary>
    public List<Entity> GetAllEnemies()
    {
        return _enemies.Values.ToList();
    }

    /// <summary>
    /// Find an enemy by ID (for combat resolution).
    /// </summary>
    public Entity? GetEnemy(string id)
    {
        _enemies.TryGetValue(id, out var enemy);
        return enemy;
    }
}
