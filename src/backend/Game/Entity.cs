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
//
// PHASE B ADDITIONS:
// Stamina system (Dark Souls-strict), classless ability loadout (pick 2 of 6),
// equipment slots, leveling/progression, enemy tagging (RuneScape-style loot),
// projectile lifetime (despawn timer), defensive abilities (shield, i-frames).
// =============================================================================

namespace Carcosa.Server.Game;

/// <summary>
/// Type of entity in the game world. Determines which game systems process it
/// and how the client renders it.
/// </summary>
public enum EntityType
{
    /// <summary>A human-controlled player character.</summary>
    Player,
    /// <summary>An AI-controlled enemy (Gronks, cultists, bosses, etc.).</summary>
    Enemy,
    /// <summary>A short-lived projectile (ember spray, void bolts, etc.).</summary>
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
    // =========================================================================
    // IDENTITY
    // =========================================================================

    /// <summary>
    /// Unique identifier. Convention: "player_{connectionId}", "enemy_{counter}", "proj_{counter}".
    /// The prefix encodes the type for quick identification in logs and on the client.
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>Broad category determining which systems process this entity.</summary>
    public EntityType Type { get; init; }

    /// <summary>
    /// Variant within the type. For players: ability-based (legacy: "gangster"/"detective"/"surgeon").
    /// For enemies: "gronk"/"cultist_acolyte"/"cultist_torch"/"boss_warehouse" etc.
    /// For projectiles: the ability that created it (e.g., "ember_spray", "void_bolt").
    /// </summary>
    public string SubType { get; set; } = "";

    // =========================================================================
    // POSITION & MOVEMENT
    // =========================================================================
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

    /// <summary>Movement speed in tiles per second (converted to tiles/tick in game loop).</summary>
    public float Speed { get; set; } = 5f;

    // =========================================================================
    // HEALTH & DEFENSE
    // =========================================================================

    /// <summary>Current hit points. Zero = dead/downed.</summary>
    public int Health { get; set; } = 100;

    /// <summary>Maximum hit points (varies by level/equipment/enemy type).</summary>
    public int MaxHealth { get; set; } = 100;

    /// <summary>Whether this entity is alive. Dead players can be revived; dead enemies are removed.</summary>
    public bool IsAlive { get; set; } = true;

    /// <summary>
    /// Flat damage reduction from armor/equipment. Incoming damage is reduced by this amount.
    /// Minimum 1 damage always passes through to prevent invincibility via stacking defense.
    /// WHY FLAT: Keeps the math simple and predictable. Players can easily understand
    /// "+5 defense = 5 less damage per hit" without percentage calculations.
    /// </summary>
    public int Defense { get; set; }

    // =========================================================================
    // STAMINA SYSTEM (Dark Souls-strict)
    // =========================================================================
    // Shared stamina bar for abilities AND sprinting. When empty, player is helpless.
    // This makes stamina the most valuable stat — mirrors Dark Souls endurance philosophy.

    /// <summary>
    /// Current stamina points. Shared resource for all abilities and sprinting.
    /// When zero, the player cannot attack or sprint until partial recovery (20% threshold).
    /// </summary>
    public float Stamina { get; set; } = 100f;

    /// <summary>
    /// Maximum stamina capacity. Scales with level: base 100 + (Level-1) * 10.
    /// WHY FLOAT: Allows fractional regen per tick without rounding issues.
    /// </summary>
    public float MaxStamina { get; set; } = 100f;

    /// <summary>
    /// Stamina regeneration rate in points per second. Converted to per-tick in StaminaSystem.
    /// Default 40/sec means full bar recovery in 2.5 seconds (after the regen delay expires).
    /// </summary>
    public float StaminaRegenRate { get; set; } = 40f;

    /// <summary>
    /// Ticks remaining before stamina begins regenerating. Set to 16 (0.8s) after any
    /// stamina-consuming action. Prevents regen during active combat sequences.
    /// WHY 0.8s: Dark Souls uses ~0.7-1.0s delay. 0.8s feels responsive but punishing.
    /// </summary>
    public int StaminaRegenDelayTicks { get; set; }

    /// <summary>
    /// True when stamina has been fully depleted. While true, the player cannot use
    /// any abilities or sprint. Clears when stamina recovers to 20% of max.
    /// WHY 20% THRESHOLD: Prevents "stutter" where player gets 1 point, attacks, depletes
    /// again immediately. Forces meaningful recovery before re-engaging.
    /// </summary>
    public bool IsStaminaDepleted { get; set; }

    // =========================================================================
    // ABILITY LOADOUT (Classless — pick 2 of 6)
    // =========================================================================
    // Players choose 1 Primary + 1 Secondary ability. Can only swap at Meditation Altars.
    // This forces strategic composition: parties benefit from diverse ability picks.

    /// <summary>
    /// ID of the equipped primary ability (e.g., "ember_spray", "pale_blade", "void_bolt").
    /// Used with Left Mouse Button. Empty string means no ability equipped.
    /// </summary>
    public string PrimaryAbility { get; set; } = "";

    /// <summary>
    /// ID of the equipped secondary ability (e.g., "warding_light", "iron_veil", "shadow_step").
    /// Used with Right Mouse Button. Empty string means no ability equipped.
    /// </summary>
    public string SecondaryAbility { get; set; } = "";

    // =========================================================================
    // EQUIPMENT SLOTS
    // =========================================================================
    // Simple slot-based equipment inspired by Link to the Past + Diablo.
    // Each slot modifies stats directly (no complex calculation chains).
    // Item IDs reference the ItemRegistry for stat lookup.

    /// <summary>Weapon slot item ID. Modifies primary ability damage.</summary>
    public string? WeaponSlot { get; set; }

    /// <summary>Armor slot item ID. Modifies max HP and defense.</summary>
    public string? ArmorSlot { get; set; }

    /// <summary>Trinket slot item ID. Modifies secondary ability stats.</summary>
    public string? TrinketSlot { get; set; }

    /// <summary>Boots slot item ID. Modifies move speed and stamina regen.</summary>
    public string? BootsSlot { get; set; }

    // =========================================================================
    // PROGRESSION
    // =========================================================================

    /// <summary>
    /// Player level (1-50 soft cap). Each level grants: +10 max stamina, +5 max HP.
    /// Stamina is THE most valuable stat gain — mirrors Dark Souls endurance importance.
    /// </summary>
    public int Level { get; set; } = 1;

    /// <summary>
    /// Current experience points. Resets to 0 on level-up (not cumulative).
    /// XP required for next level = Level * 100 (e.g., Level 2 needs 200 XP).
    /// </summary>
    public int XP { get; set; }

    // =========================================================================
    // COMBAT — TAGGING (RuneScape-style loot rights)
    // =========================================================================

    /// <summary>
    /// Peer ID of the first player to attack this enemy. Only this player (or their
    /// party members) can loot the corpse. Null if untagged (nobody has attacked yet).
    /// 
    /// WHY RUNESCAPE STYLE: Prevents kill-stealing in the open overworld. The first
    /// attacker "owns" the enemy. Party members share loot via rotation.
    /// </summary>
    public string? TaggedBy { get; set; }

    // =========================================================================
    // DEFENSIVE ABILITIES — Shield & I-Frames
    // =========================================================================

    /// <summary>
    /// Current shield hit points (from Iron Veil ability). Absorbs damage before HP.
    /// When shield reaches 0, remaining damage passes through to health.
    /// Decays to 0 after Iron Veil duration expires (tracked via SecondaryAbilityCooldown).
    /// </summary>
    public int ShieldHP { get; set; }

    /// <summary>
    /// True while entity has invincibility frames (from Shadow Step).
    /// All incoming damage is ignored during i-frames. Short duration (0.5s = 10 ticks).
    /// WHY I-FRAMES: Rewards precise timing of defensive abilities. Dark Souls rolls
    /// have i-frames; Shadow Step is our equivalent.
    /// </summary>
    public bool HasIFrames { get; set; }

    /// <summary>
    /// Ticks remaining on invincibility frames. When reaches 0, HasIFrames is cleared.
    /// Set to 10 (0.5s) when Shadow Step activates.
    /// </summary>
    public int IFrameTicks { get; set; }

    // =========================================================================
    // PROJECTILE — LIFETIME (despawn timer)
    // =========================================================================

    /// <summary>
    /// Maximum lifetime in ticks before this projectile despawns. Prevents projectiles
    /// from flying forever if they miss all targets. Calculated from Range / Speed + buffer.
    /// Only used for EntityType.Projectile.
    /// </summary>
    public int MaxLifetimeTicks { get; set; }

    /// <summary>
    /// Current lifetime in ticks (incremented each tick). When >= MaxLifetimeTicks,
    /// the projectile is removed from the game state.
    /// </summary>
    public int LifetimeTicks { get; set; }

    // =========================================================================
    // ENEMY AI STATE
    // =========================================================================
    // These fields track AI behavior for enemies (Gronks, cultists, etc.).
    // Stored on the entity to avoid a separate AI state dictionary.

    /// <summary>
    /// Ticks the enemy has been in aggro state. Used to determine when to flee.
    /// Gronks flee after 60 ticks (3s) of aggro without being hit again.
    /// </summary>
    public int AggroTicks { get; set; }

    /// <summary>X coordinate of the enemy's current wander destination (within its spawn zone).</summary>
    public float WanderTargetX { get; set; }

    /// <summary>Y coordinate of the enemy's current wander destination.</summary>
    public float WanderTargetY { get; set; }

    /// <summary>
    /// Entity ID of the current aggro target (usually the player who tagged this enemy).
    /// Null when passive/wandering. Set when attacked, cleared when fleeing completes.
    /// </summary>
    public string? AggroTargetId { get; set; }

    /// <summary>
    /// X coordinate of the enemy's spawn origin. Used for wander radius enforcement
    /// and for zone-based containment (enemy returns if pulled too far).
    /// </summary>
    public float SpawnX { get; set; }

    /// <summary>Y coordinate of the enemy's spawn origin.</summary>
    public float SpawnY { get; set; }

    // =========================================================================
    // PLAYER-SPECIFIC FIELDS (legacy + new)
    // =========================================================================

    /// <summary>
    /// For player entities: the connection ID of the controlling player.
    /// Used to route input from the correct WebSocket to this entity.
    /// Null for enemies and projectiles.
    /// </summary>
    public string? OwnerId { get; set; }

    /// <summary>
    /// Number of med kits the player is carrying. One-time use, full heal.
    /// Starting counts: Detective=3, Gangster=1, Surgeon=0.
    /// Legacy field — may be replaced by consumable items in loot system.
    /// </summary>
    public int MedKits { get; set; }

    /// <summary>
    /// True if this player entity is an invader (PvP hostile to co-op team).
    /// Invader projectiles hit co-op players, and co-op projectiles hit the invader.
    /// </summary>
    public bool IsInvader { get; set; }

    // =========================================================================
    // PROJECTILE-SPECIFIC FIELDS
    // =========================================================================

    /// <summary>For projectiles: which entity fired this (to prevent self-damage).</summary>
    public string? SourceEntityId { get; set; }

    /// <summary>For projectiles: damage dealt on hit.</summary>
    public int Damage { get; set; }

    /// <summary>For projectiles: maximum travel distance in tiles before despawning.</summary>
    public float Range { get; set; }

    /// <summary>For projectiles: accumulated distance traveled (checked against Range).</summary>
    public float DistanceTraveled { get; set; }

    // =========================================================================
    // STATE TRACKING
    // =========================================================================

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

    // =========================================================================
    // COOLDOWNS
    // =========================================================================
    // Measured in ticks (1 tick = 50ms at 20Hz). Decremented each tick by GameLoop.

    /// <summary>Ticks remaining until primary ability is available again.</summary>
    public int PrimaryFireCooldown { get; set; }

    /// <summary>Ticks remaining until secondary ability is available again.</summary>
    public int SecondaryAbilityCooldown { get; set; }

    // =========================================================================
    // METHODS
    // =========================================================================

    /// <summary>
    /// Apply damage to this entity. Accounts for i-frames, shields, and defense.
    /// Returns true if this damage killed the entity (health reached zero).
    /// 
    /// DAMAGE PIPELINE:
    ///   1. If entity has i-frames → ignore all damage (Shadow Step dodge)
    ///   2. Apply defense as flat reduction (minimum 1 damage always passes through)
    ///   3. If entity has shield HP → absorb from shield first, overflow hits health
    ///   4. Apply remaining damage to health
    ///   5. If health reaches 0 → mark dead, stop movement
    /// 
    /// WHY NOT AN EVENT: Death handling is done by GameFlowSystem which checks
    /// health after all damage in a tick. Returning bool lets the caller know
    /// immediately if additional logic (death broadcast, etc.) is needed.
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (!IsAlive) return false;

        // Step 1: I-frames grant full immunity (Shadow Step, dodge roll)
        if (HasIFrames) return false;

        // Step 2: Apply defense as flat reduction (armor/equipment)
        // Minimum 1 damage always gets through to prevent invincibility stacking
        int effectiveDamage = Math.Max(1, amount - Defense);

        // Step 3: Shield absorbs damage first (Iron Veil ability)
        if (ShieldHP > 0)
        {
            if (ShieldHP >= effectiveDamage)
            {
                // Shield absorbs all damage
                ShieldHP -= effectiveDamage;
                IsDirty = true;
                return false;
            }
            else
            {
                // Shield breaks, remaining damage hits health
                effectiveDamage -= ShieldHP;
                ShieldHP = 0;
            }
        }

        // Step 4: Apply to health
        Health = Math.Max(0, Health - effectiveDamage);
        IsDirty = true;

        // Step 5: Check for death
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
