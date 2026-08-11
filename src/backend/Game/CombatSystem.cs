// =============================================================================
// CombatSystem.cs — Weapon and Ability Processing
// =============================================================================
//
// WHY STATIC CLASS:
// CombatSystem is stateless — it reads entity state, creates projectiles, and
// applies damage. No instance data is needed. Static avoids unnecessary allocation
// and makes the API clear: pass in what you need, get the result.
//
// WHY SEPARATE FROM GAMELOOP:
// Extracting combat logic into its own file keeps GameLoop.cs focused on the
// tick pipeline orchestration. Each system file handles one concern.
//
// CLASS BALANCE PHILOSOPHY:
//   - Gangster: High volume, low damage per bullet. Area denial. Spray and pray.
//   - Detective: Low fire rate, high single-shot damage. Rewards precise aim.
//   - Surgeon: Melee damage + team healing. High risk, high reward support.
// All three are viable solo but synergize in co-op (suppression + burst + sustain).
//
// PROJECTILE MODEL:
// Weapons create projectile entities that move independently each tick.
// This means bullets have travel time (not hitscan), which creates gameplay depth
// (leading targets, dodging) and looks better with visible bullet animations.
// =============================================================================

namespace Carcosa.Server.Game;

/// <summary>
/// Handles combat actions: firing weapons, creating projectiles, and using abilities.
/// Each player class has different weapon characteristics tuned for its role.
/// 
/// WHY STATIC: No instance state needed. All data comes from the Entity being processed
/// and the GameState. This also avoids DI registration and lifetime management.
/// </summary>
public static class CombatSystem
{
    // --- Class weapon stats (all in game units) ---
    // WHY CONSTANTS: Tuning values live here for easy balance adjustments.
    // All timing values are in ticks (1 tick = 50ms at 20Hz).
    // All distances are in tiles (1 tile = 24px on client at default zoom).

    // Gangster: Tommy Gun — spray fire, high volume, low per-bullet damage
    private const int GangsterDamage = 2;
    private const float GangsterRange = 15f;          // tiles
    private const int GangsterFireCooldownTicks = 2;  // 100ms between bursts (very fast)
    private const float GangsterSpread = 0.52f;       // ~30 degrees cone in radians
    private const float GangsterAccuracy = 0.4f;      // 40% of shots are perfectly aimed
    private const int GangsterBulletsPerBurst = 3;    // 3 bullets per click
    private const float GangsterProjectileSpeed = 0.6f; // tiles per tick

    // Detective: Magnum — single precise shot, high damage, long cooldown
    private const int DetectiveDamage = 25;
    private const float DetectiveRange = 20f;         // tiles (longest range)
    private const int DetectiveFireCooldownTicks = 30; // 1.5s between shots
    private const float DetectiveAccuracy = 0.9f;     // 90% accuracy (very precise)
    private const float DetectiveProjectileSpeed = 0.8f; // tiles per tick (fastest bullet)

    // Surgeon: Dagger — instant melee hit, no projectile created
    private const int SurgeonMeleeDamage = 8;
    private const float SurgeonMeleeRange = 1.5f;     // tiles (must be adjacent)
    private const int SurgeonFireCooldownTicks = 8;   // 400ms between swings

    // Surgeon: Group Heal (secondary ability) — heals all nearby allies
    private const int SurgeonHealAmount = 15;         // HP restored per target
    private const float SurgeonHealRadius = 5f;       // tiles (generous radius)
    private const int SurgeonHealCooldownTicks = 200; // 10s cooldown (powerful ability)

    private static int _projectileCounter;

    /// <summary>
    /// Process a primary fire action for a player entity.
    /// Creates projectiles or applies melee damage based on class.
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
        }
    }

    /// <summary>
    /// Process a secondary ability action for a player entity.
    /// Currently only the Surgeon has a secondary (Group Heal).
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

            // Gangster and Detective could get secondary abilities later
            case "gangster":
                // Future: reload, grenade, etc.
                break;

            case "detective":
                // Future: dead-eye mode, etc.
                break;
        }
    }

    /// <summary>
    /// Gangster tommy gun: fires multiple projectiles in a spread cone.
    /// Low accuracy, high volume.
    /// </summary>
    private static void FireTommyGun(GameState state, Entity player, float aimAngle)
    {
        var rng = Random.Shared;

        for (int i = 0; i < GangsterBulletsPerBurst; i++)
        {
            // Apply spread and accuracy
            var spreadOffset = (rng.NextSingle() - 0.5f) * GangsterSpread;
            var accuracyMiss = rng.NextSingle() > GangsterAccuracy
                ? (rng.NextSingle() - 0.5f) * 0.5f
                : 0f;
            var finalAngle = aimAngle + spreadOffset + accuracyMiss;

            CreateProjectile(state, player, finalAngle, GangsterProjectileSpeed, GangsterDamage, GangsterRange);
        }
    }

    /// <summary>
    /// Detective magnum: fires a single precise, powerful shot.
    /// </summary>
    private static void FireMagnum(GameState state, Entity player, float aimAngle)
    {
        var rng = Random.Shared;

        // Slight accuracy variation
        var accuracyMiss = rng.NextSingle() > DetectiveAccuracy
            ? (rng.NextSingle() - 0.5f) * 0.3f
            : 0f;
        var finalAngle = aimAngle + accuracyMiss;

        CreateProjectile(state, player, finalAngle, DetectiveProjectileSpeed, DetectiveDamage, DetectiveRange);
    }

    /// <summary>
    /// Surgeon dagger: instant melee attack in the aim direction.
    /// Hits the first enemy within melee range.
    /// </summary>
    private static void MeleeDagger(GameState state, Entity player, float aimAngle)
    {
        // Check for enemies within melee range in the aim direction
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
                break; // Hit one enemy per swing
            }
        }

        // Mark player as dirty (for animation purposes on client)
        player.IsDirty = true;
    }

    /// <summary>
    /// Surgeon group heal: heals all allied players within radius.
    /// </summary>
    private static void GroupHeal(GameState state, Entity healer)
    {
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Player || !entity.IsAlive) continue;
            if (entity.Id == healer.Id) continue; // Don't heal self? Actually, let's heal self too

            var dx = entity.X - healer.X;
            var dy = entity.Y - healer.Y;
            var distSq = dx * dx + dy * dy;

            if (distSq <= SurgeonHealRadius * SurgeonHealRadius)
            {
                entity.Heal(SurgeonHealAmount);
            }
        }

        // Also heal self
        healer.Heal(SurgeonHealAmount);
    }

    /// <summary>
    /// Create a projectile entity moving in the given direction.
    /// </summary>
    private static void CreateProjectile(
        GameState state,
        Entity source,
        float angle,
        float speed,
        int damage,
        float range)
    {
        var id = $"proj_{Interlocked.Increment(ref _projectileCounter)}";

        var projectile = new Entity
        {
            Id = id,
            Type = EntityType.Projectile,
            SubType = source.SubType ?? "unknown",
            X = source.X + MathF.Cos(angle) * 0.5f, // Start slightly ahead of player
            Y = source.Y + MathF.Sin(angle) * 0.5f,
            VelocityX = MathF.Cos(angle) * speed,
            VelocityY = MathF.Sin(angle) * speed,
            Damage = damage,
            Range = range,
            SourceEntityId = source.Id,
            IsAlive = true,
            IsDirty = true,
            Health = 1,
            MaxHealth = 1
        };

        state.AddEntity(projectile);
    }
}
