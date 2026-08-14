// =============================================================================
// CombatSystem.cs — Weapon and Ability Processing
// =============================================================================
//
// OVERVIEW:
// The combat system dispatches ability usage for all player entities. It handles:
//   1. New classless ability system (Phase B) — ProcessAbility() using AbilityRegistry
//   2. Legacy class-based weapons (Phase A fallback) — ProcessPrimaryFire/ProcessSecondaryAbility
//
// DESIGN PHILOSOPHY (Phase B — Classless):
//   - All combat flows through abilities defined in AbilityRegistry
//   - Stamina is checked/drained via StaminaSystem before any ability fires
//   - Cooldowns prevent spam even with full stamina
//   - Each AbilityType has its own execution logic (projectile, melee, heal, shield, dash)
//   - Equipment stat modifiers are applied at execution time (not stored in registry)
//
// LEGACY SUPPORT (Phase A — Class-based):
//   - Gangster/Detective/Surgeon SubType routing still works for dungeon mode
//   - ProcessPrimaryFire and ProcessSecondaryAbility remain functional
//   - Will be fully deprecated once dungeons are converted to classless system
//
// PROJECTILE MODEL:
// Weapons create projectile entities that move independently each tick.
// Projectiles have both range-based AND time-based despawn (MaxLifetimeTicks)
// to prevent infinite-travel edge cases.
//
// WHY STATIC CLASS:
// CombatSystem is stateless — it reads entity state, creates projectiles, and
// applies damage. No instance data is needed. Static avoids unnecessary allocation
// and makes the API clear: pass in what you need, get the result.
// =============================================================================

using Carcosa.Server.Gameplay;

namespace Carcosa.Server.Game;

/// <summary>
/// Handles combat actions: firing abilities, creating projectiles, applying damage.
/// Supports both the new classless ability system and legacy class-based weapons.
/// 
/// WHY STATIC: No instance state needed. All data comes from the Entity being processed
/// and the GameState. This also avoids DI registration and lifetime management.
/// </summary>
public static class CombatSystem
{
    // =========================================================================
    // PROJECTILE COUNTER (thread-safe)
    // =========================================================================

    private static int _projectileCounter;

    // =========================================================================
    // NEW ABILITY SYSTEM (Phase B — Classless)
    // =========================================================================

    /// <summary>
    /// Process an ability usage by a player entity. This is the PRIMARY entry point
    /// for all combat in the classless system. Looks up the ability from the registry,
    /// validates stamina/cooldown, drains stamina, and dispatches to the appropriate
    /// execution method based on AbilityType.
    /// 
    /// EXECUTION PIPELINE:
    ///   1. Validate: entity alive? ability exists? correct slot?
    ///   2. Check cooldown (primary or secondary based on slot)
    ///   3. Check stamina via StaminaSystem.CanUseAbility
    ///   4. Drain stamina via StaminaSystem.ProcessStaminaDrain
    ///   5. Set cooldown
    ///   6. Dispatch to type-specific handler (projectile/melee/heal/shield/dash)
    /// 
    /// Returns true if the ability was successfully used (for sync/feedback purposes).
    /// </summary>
    /// <param name="state">Game state containing all entities (for targeting, projectile creation).</param>
    /// <param name="player">The player entity using the ability.</param>
    /// <param name="abilityId">ID of the ability to use (e.g., "ember_spray").</param>
    /// <param name="aimAngle">Direction in radians the player is aiming (0 = right, π/2 = down).</param>
    /// <returns>True if ability fired successfully, false if blocked by cooldown/stamina/validation.</returns>
    public static bool ProcessAbility(GameState state, Entity player, string abilityId, float aimAngle)
    {
        // --- Validation ---
        if (!player.IsAlive) return false;

        var ability = AbilityRegistry.GetAbility(abilityId);
        if (ability == null) return false;

        // --- Cooldown check ---
        // Primary abilities use PrimaryFireCooldown, secondary use SecondaryAbilityCooldown
        if (ability.Slot == AbilitySlot.Primary && player.PrimaryFireCooldown > 0) return false;
        if (ability.Slot == AbilitySlot.Secondary && player.SecondaryAbilityCooldown > 0) return false;

        // --- Stamina check and drain (equipment can reduce cost) ---
        var staminaCost = Math.Max(1f, ability.StaminaCost - player.StaminaCostReduction);
        if (!StaminaSystem.CanUseAbility(player, staminaCost)) return false;
        StaminaSystem.ProcessStaminaDrain(player, staminaCost);

        // --- Set cooldown ---
        if (ability.Slot == AbilitySlot.Primary)
            player.PrimaryFireCooldown = ability.CooldownTicks;
        else
            player.SecondaryAbilityCooldown = ability.CooldownTicks;

        // Effective damage/heal includes equipped gear bonuses on the entity
        var damage = ability.Damage + Math.Max(0, player.Damage);
        var heal = ability.HealAmount + Math.Max(0, player.BonusHealAmount);

        // --- Dispatch to ability-type handler ---
        switch (ability.Type)
        {
            case AbilityType.RangedAoE:
                ExecuteRangedAoE(state, player, ability, aimAngle, damage);
                break;

            case AbilityType.Melee:
                ExecuteMelee(state, player, ability, aimAngle, damage);
                break;

            case AbilityType.RangedSingle:
                ExecuteRangedSingle(state, player, ability, aimAngle, damage);
                break;

            case AbilityType.HealAoE:
                ExecuteHealAoE(state, player, ability, heal);
                break;

            case AbilityType.Shield:
                ExecuteShield(player, ability);
                break;

            case AbilityType.Mobility:
                ExecuteMobility(state, player, ability, aimAngle);
                break;
        }

        player.IsDirty = true;
        return true;
    }

    // =========================================================================
    // ABILITY TYPE HANDLERS (Phase B)
    // =========================================================================

    /// <summary>
    /// Ember Spray: Fire multiple projectiles in a cone spread.
    /// Each projectile does individual damage — hitting with all rewards close range.
    /// Inherits the old Tommy Gun pattern but themed as burning embers.
    /// </summary>
    private static void ExecuteRangedAoE(GameState state, Entity player, AbilityDefinition ability, float aimAngle, int damage)
    {
        var rng = Random.Shared;

        for (int i = 0; i < ability.ProjectileCount; i++)
        {
            // Distribute projectiles evenly across the cone with slight randomness
            float spreadOffset;
            if (ability.ProjectileCount == 1)
            {
                spreadOffset = 0f;
            }
            else
            {
                // Even distribution across spread angle with small random jitter
                float baseOffset = ((float)i / (ability.ProjectileCount - 1) - 0.5f) * ability.SpreadAngle;
                float jitter = (rng.NextSingle() - 0.5f) * ability.SpreadAngle * 0.15f;
                spreadOffset = baseOffset + jitter;
            }

            float finalAngle = aimAngle + spreadOffset;

            CreateProjectileWithLifetime(
                state, player, ability.Id, finalAngle,
                ability.ProjectileSpeed, damage, ability.Range);
        }
    }

    /// <summary>
    /// Pale Blade: Instant melee hit in the aim direction.
    /// Hits the FIRST enemy within range — single target, no projectile created.
    /// High damage, low cost, but requires point-blank range (risk/reward).
    /// </summary>
    private static void ExecuteMelee(GameState state, Entity player, AbilityDefinition ability, float aimAngle, int damage)
    {
        // Calculate the melee hit point (halfway along the aim direction within range)
        float meleeX = player.X + MathF.Cos(aimAngle) * ability.Range * 0.5f;
        float meleeY = player.Y + MathF.Sin(aimAngle) * ability.Range * 0.5f;

        foreach (var (_, entity) in state.Entities)
        {
            // Hit enemies (and invaders if we're not an invader, vice versa)
            if (!entity.IsAlive) continue;
            if (entity.Id == player.Id) continue;

            // Only hit enemies in overworld, or appropriate targets in dungeon
            bool isValidTarget = entity.Type == EntityType.Enemy
                || (entity.Type == EntityType.Player && entity.IsInvader != player.IsInvader);
            if (!isValidTarget) continue;

            float dx = entity.X - meleeX;
            float dy = entity.Y - meleeY;
            float distSq = dx * dx + dy * dy;

            if (distSq <= ability.Range * ability.Range)
            {
                entity.TakeDamage(damage);
                entity.IsDirty = true;

                // Set tag if this is the first hit on this enemy (RuneScape loot rights)
                if (entity.Type == EntityType.Enemy && entity.TaggedBy == null && player.OwnerId != null)
                {
                    entity.TaggedBy = player.OwnerId;
                }

                break; // Single target — hit only the first enemy in range
            }
        }
    }

    /// <summary>
    /// Void Bolt: Fire a single high-damage projectile with long range.
    /// Slow cooldown forces careful aim — missing is costly (wasted stamina + cooldown).
    /// </summary>
    private static void ExecuteRangedSingle(GameState state, Entity player, AbilityDefinition ability, float aimAngle, int damage)
    {
        CreateProjectileWithLifetime(
            state, player, ability.Id, aimAngle,
            ability.ProjectileSpeed, damage, ability.Range);
    }

    /// <summary>
    /// Warding Light: Heal self and all allied players within radius.
    /// Most expensive ability — rewards party play and careful timing.
    /// Heals self as well (solo players aren't punished for taking a heal ability).
    /// </summary>
    private static void ExecuteHealAoE(GameState state, Entity player, AbilityDefinition ability, int healAmount)
    {
        float radius = ability.AreaRadius > 0 ? ability.AreaRadius : ability.Range;
        float radiusSq = radius * radius;

        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Player || !entity.IsAlive) continue;

            // Heal all allies within radius (including self)
            float dx = entity.X - player.X;
            float dy = entity.Y - player.Y;
            float distSq = dx * dx + dy * dy;

            if (distSq <= radiusSq)
            {
                entity.Heal(healAmount);
            }
        }

        // Always heal self even if somehow outside own radius (safety)
        player.Heal(healAmount);
    }

    /// <summary>
    /// Iron Veil: Apply a temporary damage-absorbing shield.
    /// Shield HP is tracked on the Entity. Damage hits shield first, then health.
    /// Shield expires after DurationTicks even if not fully consumed.
    /// 
    /// WHY NOT PERMANENT: A permanent shield would be overpowered. Time-limited
    /// shields reward reactive play — activate just before a big hit.
    /// </summary>
    private static void ExecuteShield(Entity player, AbilityDefinition ability)
    {
        player.ShieldHP = ability.ShieldAmount;
        // Duration is enforced by the game tick loop checking SecondaryAbilityCooldown
        // The shield lasts for DurationTicks (separate from the cooldown which is longer)
        // We'll track shield expiry in the combat tick — for now, shield persists until
        // either broken by damage or cleared by duration tracking in the game loop
    }

    /// <summary>
    /// Shadow Step: Teleport a short distance in the aim direction with i-frames.
    /// The dash grants brief invincibility — timing it to dodge an attack is the skill expression.
    /// Validates the destination is walkable (can't dash through walls).
    /// 
    /// WHY TELEPORT NOT LERP: Instant teleport is simpler to implement and sync across
    /// peers. A smooth dash animation is purely cosmetic and handled client-side.
    /// </summary>
    private static void ExecuteMobility(GameState state, Entity player, AbilityDefinition ability, float aimAngle)
    {
        float dashDistance = ability.Range;
        float targetX = player.X + MathF.Cos(aimAngle) * dashDistance;
        float targetY = player.Y + MathF.Sin(aimAngle) * dashDistance;

        // Validate destination is walkable (if we have a map)
        if (state.Map != null)
        {
            // Try the full dash distance first
            if (state.Map.IsWalkableF(targetX, targetY))
            {
                player.X = targetX;
                player.Y = targetY;
            }
            else
            {
                // Binary search for the furthest walkable point along the dash vector
                // This prevents getting stuck on walls while still dashing as far as possible
                float validDistance = FindMaxWalkableDistance(state.Map, player.X, player.Y, aimAngle, dashDistance);
                if (validDistance > 0.5f) // Only move if we can dash at least half a tile
                {
                    player.X += MathF.Cos(aimAngle) * validDistance;
                    player.Y += MathF.Sin(aimAngle) * validDistance;
                }
            }
        }
        else
        {
            // No map collision available (e.g., overworld before map loaded) — just teleport
            player.X = targetX;
            player.Y = targetY;
        }

        // Grant invincibility frames
        player.HasIFrames = true;
        player.IFrameTicks = ability.DurationTicks;
    }

    /// <summary>
    /// Binary search along a ray to find the maximum walkable distance.
    /// Used by Shadow Step to dash as far as possible without clipping into walls.
    /// Checks 8 increments along the path — good enough precision for 3-tile dashes.
    /// </summary>
    private static float FindMaxWalkableDistance(TileMap map, float startX, float startY, float angle, float maxDist)
    {
        const int steps = 8;
        float bestDist = 0f;

        for (int i = 1; i <= steps; i++)
        {
            float testDist = maxDist * i / steps;
            float testX = startX + MathF.Cos(angle) * testDist;
            float testY = startY + MathF.Sin(angle) * testDist;

            if (map.IsWalkableF(testX, testY))
            {
                bestDist = testDist;
            }
            else
            {
                break; // Stop at first wall — don't skip over walls
            }
        }

        return bestDist;
    }

    // =========================================================================
    // PROJECTILE CREATION (shared by ability handlers)
    // =========================================================================

    /// <summary>
    /// Create a projectile entity with both range-based and time-based despawn.
    /// The lifetime prevents projectiles from existing forever in edge cases
    /// (e.g., moving platforms or map boundaries that don't trigger collision).
    /// 
    /// SubType is set to the ability ID so the client knows how to render it
    /// (different visual for ember_spray vs void_bolt projectiles).
    /// </summary>
    private static void CreateProjectileWithLifetime(
        GameState state, Entity source, string abilityId,
        float angle, float speed, int damage, float range)
    {
        var id = $"proj_{Interlocked.Increment(ref _projectileCounter)}";

        // Calculate lifetime: time to travel full range + 10 tick buffer
        // This ensures projectiles despawn even if range-check has floating point issues
        int lifetimeTicks = speed > 0f ? (int)(range / speed) + 10 : 60;

        var projectile = new Entity
        {
            Id = id,
            Type = EntityType.Projectile,
            SubType = abilityId, // Used by client for visual rendering
            X = source.X + MathF.Cos(angle) * 0.5f, // Start slightly ahead of source
            Y = source.Y + MathF.Sin(angle) * 0.5f,
            VelocityX = MathF.Cos(angle) * speed,
            VelocityY = MathF.Sin(angle) * speed,
            Damage = damage,
            Range = range,
            SourceEntityId = source.Id,
            MaxLifetimeTicks = lifetimeTicks,
            LifetimeTicks = 0,
            IsAlive = true,
            IsDirty = true,
            Health = 1,
            MaxHealth = 1,
        };

        state.AddEntity(projectile);
    }

    // =========================================================================
    // LEGACY CLASS-BASED SYSTEM (Phase A — Dungeon Fallback)
    // =========================================================================
    // These methods are kept for backward compatibility with the dungeon system
    // which still uses SubType-based class routing. They will be deprecated once
    // dungeons are converted to the classless ability system.

    // --- Legacy class weapon stats ---
    private const int GangsterDamage = 2;
    private const float GangsterRange = 15f;
    private const int GangsterFireCooldownTicks = 2;
    private const float GangsterSpread = 0.52f;
    private const float GangsterAccuracy = 0.4f;
    private const int GangsterBulletsPerBurst = 3;
    private const float GangsterProjectileSpeed = 0.6f;

    private const int DetectiveDamage = 25;
    private const float DetectiveRange = 20f;
    private const int DetectiveFireCooldownTicks = 30;
    private const float DetectiveAccuracy = 0.9f;
    private const float DetectiveProjectileSpeed = 0.8f;

    private const int SurgeonMeleeDamage = 8;
    private const float SurgeonMeleeRange = 1.5f;
    private const int SurgeonFireCooldownTicks = 8;

    private const int SurgeonHealAmount = 15;
    private const float SurgeonHealRadius = 5f;
    private const int SurgeonHealCooldownTicks = 200;

    /// <summary>
    /// [LEGACY] Process a primary fire action using class-based SubType routing.
    /// Kept for dungeon mode backward compatibility.
    /// </summary>
    public static void ProcessPrimaryFire(GameState state, Entity player, float aimAngle)
    {
        if (!player.IsAlive || player.PrimaryFireCooldown > 0) return;

        switch (player.SubType)
        {
            case "gangster":
                FireTommyGun(state, player, aimAngle);
                player.PrimaryFireCooldown = GangsterFireCooldownTicks;
                break;

            case "detective":
                FireMagnum(state, player, aimAngle);
                player.PrimaryFireCooldown = DetectiveFireCooldownTicks;
                break;

            case "surgeon":
                MeleeDagger(state, player, aimAngle);
                player.PrimaryFireCooldown = SurgeonFireCooldownTicks;
                break;

            default:
                // If SubType matches an ability ID, use the new system instead
                if (AbilityRegistry.Exists(player.SubType))
                {
                    ProcessAbility(state, player, player.SubType, aimAngle);
                }
                break;
        }
    }

    /// <summary>
    /// [LEGACY] Process a secondary ability action using class-based SubType routing.
    /// Kept for dungeon mode backward compatibility.
    /// </summary>
    public static void ProcessSecondaryAbility(GameState state, Entity player)
    {
        if (!player.IsAlive || player.SecondaryAbilityCooldown > 0) return;

        switch (player.SubType)
        {
            case "surgeon":
                GroupHeal(state, player);
                player.SecondaryAbilityCooldown = SurgeonHealCooldownTicks;
                break;

            case "gangster":
            case "detective":
                break;
        }
    }

    // =========================================================================
    // LEGACY WEAPON IMPLEMENTATIONS
    // =========================================================================

    /// <summary>[LEGACY] Gangster tommy gun: fires multiple projectiles in a spread cone.</summary>
    private static void FireTommyGun(GameState state, Entity player, float aimAngle)
    {
        var rng = Random.Shared;

        for (int i = 0; i < GangsterBulletsPerBurst; i++)
        {
            var spreadOffset = (rng.NextSingle() - 0.5f) * GangsterSpread;
            var accuracyMiss = rng.NextSingle() > GangsterAccuracy
                ? (rng.NextSingle() - 0.5f) * 0.5f
                : 0f;
            var finalAngle = aimAngle + spreadOffset + accuracyMiss;

            CreateProjectile(state, player, finalAngle, GangsterProjectileSpeed, GangsterDamage, GangsterRange);
        }
    }

    /// <summary>[LEGACY] Detective magnum: fires a single precise, powerful shot.</summary>
    private static void FireMagnum(GameState state, Entity player, float aimAngle)
    {
        var rng = Random.Shared;

        var accuracyMiss = rng.NextSingle() > DetectiveAccuracy
            ? (rng.NextSingle() - 0.5f) * 0.3f
            : 0f;
        var finalAngle = aimAngle + accuracyMiss;

        CreateProjectile(state, player, finalAngle, DetectiveProjectileSpeed, DetectiveDamage, DetectiveRange);
    }

    /// <summary>[LEGACY] Surgeon dagger: instant melee attack in the aim direction.</summary>
    private static void MeleeDagger(GameState state, Entity player, float aimAngle)
    {
        var meleeX = player.X + MathF.Cos(aimAngle) * SurgeonMeleeRange * 0.5f;
        var meleeY = player.Y + MathF.Sin(aimAngle) * SurgeonMeleeRange * 0.5f;

        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Enemy || !entity.IsAlive) continue;

            var dx = entity.X - meleeX;
            var dy = entity.Y - meleeY;
            var distSq = dx * dx + dy * dy;

            if (distSq <= SurgeonMeleeRange * SurgeonMeleeRange)
            {
                entity.TakeDamage(SurgeonMeleeDamage);
                entity.IsDirty = true;
                break;
            }
        }

        player.IsDirty = true;
    }

    /// <summary>[LEGACY] Surgeon group heal: heals all allied players within radius.</summary>
    private static void GroupHeal(GameState state, Entity healer)
    {
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Player || !entity.IsAlive) continue;

            var dx = entity.X - healer.X;
            var dy = entity.Y - healer.Y;
            var distSq = dx * dx + dy * dy;

            if (distSq <= SurgeonHealRadius * SurgeonHealRadius)
            {
                entity.Heal(SurgeonHealAmount);
            }
        }

        healer.Heal(SurgeonHealAmount);
    }

    /// <summary>[LEGACY] Create a projectile entity (old system without lifetime tracking).</summary>
    private static void CreateProjectile(
        GameState state, Entity source,
        float angle, float speed, int damage, float range)
    {
        var id = $"proj_{Interlocked.Increment(ref _projectileCounter)}";

        var projectile = new Entity
        {
            Id = id,
            Type = EntityType.Projectile,
            SubType = source.SubType ?? "unknown",
            X = source.X + MathF.Cos(angle) * 0.5f,
            Y = source.Y + MathF.Sin(angle) * 0.5f,
            VelocityX = MathF.Cos(angle) * speed,
            VelocityY = MathF.Sin(angle) * speed,
            Damage = damage,
            Range = range,
            SourceEntityId = source.Id,
            MaxLifetimeTicks = speed > 0f ? (int)(range / speed) + 10 : 60,
            LifetimeTicks = 0,
            IsAlive = true,
            IsDirty = true,
            Health = 1,
            MaxHealth = 1,
        };

        state.AddEntity(projectile);
    }
}
