// =============================================================================
// Entity.cs — Game Entity Model
// =============================================================================
//
// WHY A SINGLE ENTITY CLASS:
// All game objects (players, enemies, projectiles) share the same Entity class
// rather than using inheritance or an ECS (Entity Component System). This is because:
//   1. The game is relatively simple — 3 entity types with similar data needs
//   2. Avoids polymorphic serialization issues with AOT
//   3. The server-authoritative model needs fast iteration over all entities in a
//      flat collection (ConcurrentDictionary) without type checks
//   4. The SubType string provides variant behavior within each EntityType
//
// WHY NOT ECS:
// A full ECS (Arch, DefaultEcs, etc.) adds library dependencies that may not be
// AOT-friendly and is overkill for <100 entities. The simple flat model with
// SubType-based behavior (via switch statements in systems) is efficient and
// easy to understand.
//
// FIELD LAYOUT:
// Fields are grouped by concern (identity, position, health, combat, state tracking).
// Some fields are only used by certain entity types (e.g., SourceEntityId only for
// projectiles) — this wastes a few bytes per entity but avoids type hierarchies.
// =============================================================================

namespace Carcosa.Server.Game;

/// <summary>
/// Type of entity in the game world. Determines which game systems process it
/// and how the client renders it.
/// </summary>
public enum EntityType
{
    /// <summary>A human-controlled player character (gangster, detective, or surgeon).</summary>
    Player,
    /// <summary>An AI-controlled enemy (various cultist types and bosses).</summary>
    Enemy,
    /// <summary>A short-lived projectile (bullets, daggers, eldritch bolts).</summary>
    Projectile
}

/// <summary>
/// Represents a game entity with position, velocity, and health.
/// This is the server-authoritative state for each entity in the world.
/// 
/// WHY SEALED: Prevents inheritance (which we don't need) and allows the JIT/AOT
/// to devirtualize method calls. All behavioral variation is handled by SubType
/// and switch statements in the game systems.
/// 
/// WHY NOT A STRUCT: Entities are stored in ConcurrentDictionary and passed by
/// reference to multiple systems. Struct semantics (copy-on-pass) would be incorrect.
/// </summary>
public sealed class Entity
{
    // --- Identity ---

    /// <summary>
    /// Unique identifier. Convention: "player_{connectionId}", "enemy_{counter}", "proj_{counter}".
    /// The prefix encodes the type for quick identification in logs and on the client.
    /// </summary>
    public string Id { get; init; } = "";
    /// <summary>Broad category determining which systems process this entity.</summary>
    public EntityType Type { get; init; }
    /// <summary>
    /// Variant within the type. For players: "gangster"/"detective"/"surgeon".
    /// For enemies: "cultist_acolyte"/"cultist_torch"/"boss_warehouse" etc.
    /// For projectiles: the class that fired it (for rendering different bullet styles).
    /// </summary>
    public string SubType { get; set; } = "";

    // --- Position & Movement ---
    // Using float tile coordinates for sub-tile precision.
    // (0,0) is top-left. Each integer = one tile. Movement is in tiles/tick.

    /// <summary>X position in tile coordinates (sub-tile precision via float).</summary>
    public float X { get; set; }
    /// <summary>Y position in tile coordinates.</summary>
    public float Y { get; set; }
    /// <summary>Horizontal velocity in tiles per tick (set each tick from input or AI).</summary>
    public float VelocityX { get; set; }
    /// <summary>Vertical velocity in tiles per tick.</summary>
    public float VelocityY { get; set; }

    // --- Health ---

    /// <summary>Current hit points. Zero = dead/downed.</summary>
    public int Health { get; set; } = 100;
    /// <summary>Maximum hit points (varies by class/enemy type).</summary>
    public int MaxHealth { get; set; } = 100;
    /// <summary>Whether this entity is alive. Dead players can be revived; dead enemies are removed.</summary>
    public bool IsAlive { get; set; } = true;

    // --- Movement Configuration ---

    /// <summary>Movement speed in tiles per second (converted to tiles/tick in game loop).</summary>
    public float Speed { get; set; } = 5f;

    // --- Player-specific fields ---

    /// <summary>
    /// For player entities: the connection ID of the controlling player.
    /// Used to route input from the correct WebSocket to this entity.
    /// Null for enemies and projectiles.
    /// </summary>
    public string? OwnerId { get; set; }

    // --- Projectile-specific fields ---

    /// <summary>For projectiles: which entity fired this (to prevent self-damage).</summary>
    public string? SourceEntityId { get; set; }
    /// <summary>For projectiles: damage dealt on hit.</summary>
    public int Damage { get; set; }
    /// <summary>For projectiles: maximum travel distance in tiles before despawning.</summary>
    public float Range { get; set; }
    /// <summary>For projectiles: accumulated distance traveled (checked against Range).</summary>
    public float DistanceTraveled { get; set; }

    // --- State Tracking ---

    /// <summary>
    /// Dirty flag for delta broadcasting. Set to true whenever any state changes.
    /// The game loop only sends entities with IsDirty=true to clients, then clears all flags.
    /// This is the core optimization that makes 20Hz updates viable with many entities.
    /// </summary>
    public bool IsDirty { get; set; } = true;
    /// <summary>
    /// For players: the sequence number of the last processed input.
    /// Echoed back in GameStatePayload so the client can reconcile predictions.
    /// </summary>
    public int LastProcessedInput { get; set; }

    // --- Cooldowns ---
    // Measured in ticks (1 tick = 50ms at 20Hz). Decremented each tick by GameLoop.

    /// <summary>Ticks remaining until primary fire is available again.</summary>
    public int PrimaryFireCooldown { get; set; }
    /// <summary>Ticks remaining until secondary ability is available again.</summary>
    public int SecondaryAbilityCooldown { get; set; }

    // --- Items ---

    /// <summary>
    /// Number of med kits the player is carrying. One-time use, full heal.
    /// Starting counts: Detective=3, Gangster=1, Surgeon=0.
    /// </summary>
    public int MedKits { get; set; }

    // --- Invader ---

    /// <summary>
    /// True if this player entity is an invader (PvP hostile to co-op team).
    /// Invader projectiles hit co-op players, and co-op projectiles hit the invader.
    /// </summary>
    public bool IsInvader { get; set; }

    // --- Methods ---

    /// <summary>
    /// Apply damage to this entity. Clamps health to zero and sets death state.
    /// Returns true if this damage killed the entity (health reached zero).
    /// 
    /// WHY NOT AN EVENT: Death handling is done by GameFlowSystem which checks
    /// health after all damage in a tick. Returning bool lets the caller know
    /// immediately if additional logic (death broadcast, etc.) is needed.
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (!IsAlive) return false;

        Health = Math.Max(0, Health - amount);
        IsDirty = true;

        if (Health <= 0)
        {
            IsAlive = false;
            VelocityX = 0;
            VelocityY = 0;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Heal this entity. Clamps at max health. No-op if dead (must be revived first).
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsAlive) return;
        Health = Math.Min(MaxHealth, Health + amount);
        IsDirty = true;
    }
}
