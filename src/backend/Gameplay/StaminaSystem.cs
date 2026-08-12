// =============================================================================
// StaminaSystem.cs — Dark Souls-Strict Stamina Management
// =============================================================================
//
// DESIGN PHILOSOPHY (Dark Souls endurance):
// Stamina is the SINGLE MOST IMPORTANT RESOURCE in combat. It's shared between
// all abilities AND sprinting, creating constant tension between offense, defense,
// and mobility. An empty stamina bar means complete helplessness — no attacks,
// no sprints, no dodges — until partial recovery.
//
// WHY STRICT:
// Dark Souls' stamina system works because it forces deliberate play. Every action
// has a cost, and greedy players are punished with a "helpless" window where they
// can't do anything. This creates emergent difficulty without requiring complex AI.
//
// REGEN MECHANICS:
//   - 0.8 second delay after ANY stamina-consuming action before regen starts
//   - Base regen: 40 points/second (2.5s for full bar at level 1)
//   - Depletion threshold: must recover to 20% before actions are available again
//   - Sprint continuously drains while active (15/sec = rapid depletion)
//
// LEVEL SCALING:
//   Stamina is THE most valuable stat gain per level (like Dark Souls endurance).
//   Each level adds +10 max stamina. Regen rate also scales slightly (+0.5/level).
//   At level 50: MaxStamina = 590, RegenRate = 64.5/sec — significantly stronger.
//
// TICK RATE:
//   All calculations are per-tick (1 tick = 50ms at 20Hz).
//   Regen per tick = StaminaRegenRate / 20.
//   Sprint drain per tick = 15 / 20 = 0.75.
//   Regen delay = 16 ticks = 0.8 seconds.
//
// WHY STATIC CLASS:
// StaminaSystem is stateless — it reads/writes Entity fields directly.
// No instance data needed. Static keeps the API clean and avoids DI overhead.
// =============================================================================

using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Processes stamina drain, regeneration, and related state (i-frames, shields)
/// for player entities. Called once per tick for each active player.
/// </summary>
public static class StaminaSystem
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>
    /// Ticks before stamina begins regenerating after last stamina-consuming action.
    /// 16 ticks × 50ms = 0.8 seconds. Matches Dark Souls' feel of brief vulnerability.
    /// </summary>
    private const int RegenDelayTicks = 16;

    /// <summary>
    /// Fraction of max stamina that must be recovered before actions are available
    /// again after full depletion. Prevents stutter-attacking (use 1 point, attack,
    /// deplete, repeat). Forces meaningful recovery window.
    /// </summary>
    private const float DepletionRecoveryThreshold = 0.2f;

    /// <summary>
    /// Sprint stamina drain in points per second. At 20Hz = 0.75 per tick.
    /// Running continuously for ~6.7 seconds depletes a level-1 bar (100 points).
    /// </summary>
    private const float SprintDrainPerSecond = 15f;

    /// <summary>
    /// Tick rate used for per-tick conversions. 20 ticks per second.
    /// </summary>
    private const float TicksPerSecond = 20f;

    /// <summary>
    /// Base max stamina at level 1. Foundation for all stamina calculations.
    /// </summary>
    private const float BaseMaxStamina = 100f;

    /// <summary>
    /// Max stamina gained per level. At level 50: 100 + 49*10 = 590 total.
    /// WHY 10: Makes each level feel significant (+10% at low levels, still meaningful at high).
    /// </summary>
    private const float StaminaPerLevel = 10f;

    /// <summary>
    /// Base stamina regen rate at level 1 (points per second).
    /// </summary>
    private const float BaseRegenRate = 40f;

    /// <summary>
    /// Additional regen rate gained per level (points per second per level).
    /// At level 50: 40 + 49*0.5 = 64.5/sec — noticeably faster recovery.
    /// </summary>
    private const float RegenRatePerLevel = 0.5f;

    // =========================================================================
    // PUBLIC METHODS
    // =========================================================================

    /// <summary>
    /// Drain stamina when an ability is used. Sets the regen delay and marks
    /// depletion state if stamina reaches zero.
    /// 
    /// Called by CombatSystem when an ability successfully fires.
    /// </summary>
    /// <param name="entity">The player entity using stamina.</param>
    /// <param name="cost">Stamina cost of the ability used.</param>
    public static void ProcessStaminaDrain(Entity entity, float cost)
    {
        entity.Stamina = MathF.Max(0f, entity.Stamina - cost);
        entity.StaminaRegenDelayTicks = RegenDelayTicks;
        entity.IsDirty = true;

        // If stamina is fully depleted, enter "helpless" state
        if (entity.Stamina <= 0f)
        {
            entity.IsStaminaDepleted = true;
        }
    }

    /// <summary>
    /// Process stamina regeneration for one tick. Handles regen delay countdown
    /// and recovery from depletion state.
    /// 
    /// Called once per tick for every active player entity (by the game loop or
    /// overworld combat tick on the shard host).
    /// </summary>
    /// <param name="entity">The player entity to process.</param>
    public static void ProcessStaminaTick(Entity entity)
    {
        // Calculate level-scaled max stamina and regen rate
        float maxStamina = GetMaxStaminaForLevel(entity.Level);
        float regenRate = GetRegenRateForLevel(entity.Level);

        // Update the entity's max stamina (in case level changed)
        entity.MaxStamina = maxStamina;

        // Countdown regen delay — no regen until delay expires
        if (entity.StaminaRegenDelayTicks > 0)
        {
            entity.StaminaRegenDelayTicks--;
            return;
        }

        // If stamina is already full, nothing to do
        if (entity.Stamina >= maxStamina)
        {
            entity.Stamina = maxStamina; // Clamp (in case max decreased)
            return;
        }

        // Apply regen: rate is per-second, divide by ticks-per-second for per-tick
        float regenPerTick = regenRate / TicksPerSecond;
        entity.Stamina = MathF.Min(maxStamina, entity.Stamina + regenPerTick);
        entity.IsDirty = true;

        // Check if we've recovered enough to exit depletion state
        // Must reach 20% of max before player can act again
        if (entity.IsStaminaDepleted && entity.Stamina >= maxStamina * DepletionRecoveryThreshold)
        {
            entity.IsStaminaDepleted = false;
        }
    }

    /// <summary>
    /// Drain stamina for sprinting (called each tick while sprint is held).
    /// Sprint drain is continuous and also resets the regen delay each tick,
    /// meaning regen only starts 0.8s AFTER the player stops sprinting.
    /// 
    /// WHY RESET DELAY EACH TICK: Prevents any regen during sprint. If we only
    /// set the delay once at sprint start, regen would begin mid-sprint after 0.8s.
    /// </summary>
    /// <param name="entity">The sprinting player entity.</param>
    public static void ProcessSprintDrain(Entity entity)
    {
        float drainPerTick = SprintDrainPerSecond / TicksPerSecond;
        entity.Stamina = MathF.Max(0f, entity.Stamina - drainPerTick);
        entity.StaminaRegenDelayTicks = RegenDelayTicks;
        entity.IsDirty = true;

        if (entity.Stamina <= 0f)
        {
            entity.IsStaminaDepleted = true;
        }
    }

    /// <summary>
    /// Check whether a player can use an ability with the given stamina cost.
    /// Returns false if depleted (must recover to 20%) or if insufficient stamina.
    /// 
    /// WHY SEPARATE FROM DRAIN: Allows CombatSystem to check before committing
    /// to ability processing (animations, cooldowns, etc.). Fail-fast pattern.
    /// </summary>
    /// <param name="entity">The player entity attempting to use an ability.</param>
    /// <param name="cost">Stamina cost of the desired ability.</param>
    /// <returns>True if the ability can be used, false if stamina is insufficient or depleted.</returns>
    public static bool CanUseAbility(Entity entity, float cost)
    {
        // Depleted state: can't do ANYTHING until recovery threshold is reached
        if (entity.IsStaminaDepleted) return false;

        // Must have at least the cost available
        return entity.Stamina >= cost;
    }

    /// <summary>
    /// Process i-frame countdown. When IFrameTicks reaches 0, invincibility ends.
    /// Called each tick for entities that have active i-frames (Shadow Step).
    /// </summary>
    /// <param name="entity">The entity with active i-frames.</param>
    public static void ProcessIFrameTick(Entity entity)
    {
        if (!entity.HasIFrames) return;

        if (entity.IFrameTicks > 0)
        {
            entity.IFrameTicks--;
        }

        if (entity.IFrameTicks <= 0)
        {
            entity.HasIFrames = false;
            entity.IsDirty = true;
        }
    }

    /// <summary>
    /// Process shield decay. Iron Veil's shield lasts for a fixed duration tracked
    /// via SecondaryAbilityCooldown. When the duration expires (a separate timer),
    /// any remaining shield HP is removed. This method handles tick-based shield
    /// expiry by checking if the shield should still be active.
    /// 
    /// NOTE: Shield duration is managed by tracking ticks since activation.
    /// The CombatSystem sets ShieldHP and a duration counter. This method clears
    /// the shield when it expires. For simplicity, we use a dedicated field or
    /// piggyback on the cooldown mechanism — here we just ensure if shield is
    /// active with no i-frames context, it gets cleared after its duration.
    /// The actual duration tracking is done in CombatSystem via cooldown.
    /// </summary>
    /// <param name="entity">The entity with an active shield.</param>
    /// <param name="shieldDurationTicks">Total duration the shield should last.</param>
    /// <param name="ticksSinceActivation">How many ticks since the shield was activated.</param>
    public static void ClearExpiredShield(Entity entity, int shieldDurationTicks, int ticksSinceActivation)
    {
        if (entity.ShieldHP <= 0) return;

        if (ticksSinceActivation >= shieldDurationTicks)
        {
            entity.ShieldHP = 0;
            entity.IsDirty = true;
        }
    }

    // =========================================================================
    // LEVEL SCALING HELPERS
    // =========================================================================

    /// <summary>
    /// Calculate max stamina for a given level.
    /// Formula: 100 + (level - 1) * 10
    /// Level 1 = 100, Level 10 = 190, Level 50 = 590
    /// </summary>
    public static float GetMaxStaminaForLevel(int level)
    {
        return BaseMaxStamina + (level - 1) * StaminaPerLevel;
    }

    /// <summary>
    /// Calculate stamina regen rate for a given level (points per second).
    /// Formula: 40 + (level - 1) * 0.5
    /// Level 1 = 40/sec, Level 10 = 44.5/sec, Level 50 = 64.5/sec
    /// </summary>
    public static float GetRegenRateForLevel(int level)
    {
        return BaseRegenRate + (level - 1) * RegenRatePerLevel;
    }

    /// <summary>
    /// Check if the entity can sprint (has stamina and isn't depleted).
    /// Sprint requires any stamina remaining (even 0.1 is enough to start).
    /// </summary>
    public static bool CanSprint(Entity entity)
    {
        return !entity.IsStaminaDepleted && entity.Stamina > 0f;
    }
}
