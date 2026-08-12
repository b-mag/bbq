// =============================================================================
// EnemyAI.cs — Overworld Enemy Behavior System
// =============================================================================
//
// OVERVIEW:
// Processes AI behavior for all overworld enemies each tick. Enemies have three
// behavioral states: Passive (wandering), Aggro (attacking), and Fleeing.
//
// GRONK BEHAVIOR:
//   PASSIVE: Wander randomly within spawn zone. Peaceful unless attacked.
//     - 2% chance per tick to pick a new wander target within 8 tiles of spawn
//     - Move toward wander target at slow speed (1.5 tiles/sec)
//     - Stop when reaching target (within 0.5 tiles)
//
//   AGGRO: Triggered when TaggedBy is set (player attacked this enemy).
//     - Chase the aggro target (player who attacked) at faster speed (2.5 tiles/sec)
//     - When within attack range (1.2 tiles), peck attack (5 damage, 20-tick cooldown)
//     - Track aggro duration — after 60 ticks (3s) without being hit, transition to Flee
//
//   FLEE: Brief retreat after aggro timeout.
//     - Move directly away from the aggro target for 60 ticks (3s)
//     - Then clear aggro state and return to Passive
//     - Clear TaggedBy so the enemy becomes untagged (can be re-tagged)
//
// WHY STATIC:
// EnemyAI is stateless — all state lives on the Entity. Static avoids allocation
// and makes the per-tick cost minimal. Called once per tick for each alive enemy.
//
// DESIGN PHILOSOPHY:
// Gronks are "beginner enemies" — passive until provoked, weak in combat, and
// flee after brief engagement. They teach new players:
//   1. How combat works (attack → enemy responds)
//   2. Stamina management (can't infinitely attack)
//   3. Tagging/loot mechanics (first attacker gets loot)
// Without being punishing or frustrating.
// =============================================================================

using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Processes AI behavior for overworld enemies. Called per-tick on the shard host.
/// Each enemy entity stores its own behavioral state (AggroTicks, WanderTarget, etc.).
/// </summary>
public static class EnemyAI
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Speed while wandering passively (tiles per second).</summary>
    private const float WanderSpeed = 1.5f;

    /// <summary>Speed while chasing the aggro target (tiles per second).</summary>
    private const float ChaseSpeed = 2.5f;

    /// <summary>Speed while fleeing from the aggro target (tiles per second).</summary>
    private const float FleeSpeed = 3.0f;

    /// <summary>Maximum wander distance from spawn point (tiles).</summary>
    private const float WanderRadius = 8f;

    /// <summary>Distance threshold to consider "at" the wander target (tiles).</summary>
    private const float ArrivalThreshold = 0.5f;

    /// <summary>Chance per tick (at 20Hz) to pick a new wander target. 2% = new target ~every 2.5s.</summary>
    private const float WanderChance = 0.02f;

    /// <summary>Attack range for melee peck (tiles).</summary>
    private const float AttackRange = 1.2f;

    /// <summary>Cooldown between peck attacks (ticks). 20 ticks = 1 second.</summary>
    private const int AttackCooldownTicks = 20;

    /// <summary>Damage per peck attack.</summary>
    private const int PeckDamage = 5;

    /// <summary>
    /// Ticks of aggro before transitioning to flee (if not hit again).
    /// 60 ticks = 3 seconds of chasing before giving up.
    /// </summary>
    private const int AggroTimeoutTicks = 60;

    /// <summary>
    /// Ticks spent fleeing before returning to passive.
    /// 60 ticks = 3 seconds of running away.
    /// </summary>
    private const int FleeDurationTicks = 60;

    /// <summary>
    /// AggroTicks value that indicates "fleeing" state (above timeout means fleeing).
    /// We use AggroTicks > AggroTimeoutTicks to mean "in flee mode".
    /// </summary>
    private const int FleeStartTick = AggroTimeoutTicks;

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>
    /// Process one AI tick for an enemy entity. Handles state transitions and
    /// movement/attack behavior based on current aggro state.
    /// 
    /// STATE MACHINE:
    ///   AggroTargetId == null → PASSIVE (wander)
    ///   AggroTargetId != null and AggroTicks &lt; AggroTimeoutTicks → AGGRO (chase + attack)
    ///   AggroTargetId != null and AggroTicks >= AggroTimeoutTicks → FLEE (retreat)
    ///   AggroTicks >= AggroTimeoutTicks + FleeDurationTicks → reset to PASSIVE
    /// </summary>
    /// <param name="enemy">The enemy entity to process.</param>
    /// <param name="findTarget">Function to find a target entity by ID (for chasing).</param>
    public static void ProcessTick(Entity enemy, Func<string, Entity?> findTarget)
    {
        if (!enemy.IsAlive) return;

        // Decrement attack cooldown
        if (enemy.PrimaryFireCooldown > 0)
        {
            enemy.PrimaryFireCooldown--;
        }

        // Determine state and process
        if (enemy.AggroTargetId == null)
        {
            ProcessPassive(enemy);
        }
        else if (enemy.AggroTicks < AggroTimeoutTicks)
        {
            ProcessAggro(enemy, findTarget);
        }
        else
        {
            ProcessFlee(enemy, findTarget);
        }
    }

    /// <summary>
    /// Notify an enemy that it was attacked (for aggro triggering).
    /// Sets the aggro target and resets aggro ticks.
    /// Called by combat resolution when a projectile/melee hits this enemy.
    /// </summary>
    /// <param name="enemy">The enemy that was hit.</param>
    /// <param name="attackerId">Entity ID of the attacker.</param>
    public static void NotifyAttacked(Entity enemy, string attackerId)
    {
        if (!enemy.IsAlive) return;

        enemy.AggroTargetId = attackerId;
        enemy.AggroTicks = 0; // Reset aggro timer (attacking refreshes the chase duration)
    }

    // =========================================================================
    // BEHAVIORAL STATES
    // =========================================================================

    /// <summary>
    /// PASSIVE: Wander randomly near spawn point. Peaceful.
    /// </summary>
    private static void ProcessPassive(Entity enemy)
    {
        var rng = Random.Shared;

        // Chance to pick new wander target
        if (rng.NextSingle() < WanderChance)
        {
            // Random point within WanderRadius of spawn
            float angle = rng.NextSingle() * MathF.PI * 2f;
            float dist = rng.NextSingle() * WanderRadius;
            enemy.WanderTargetX = enemy.SpawnX + MathF.Cos(angle) * dist;
            enemy.WanderTargetY = enemy.SpawnY + MathF.Sin(angle) * dist;
        }

        // Move toward wander target
        MoveToward(enemy, enemy.WanderTargetX, enemy.WanderTargetY, WanderSpeed);
    }

    /// <summary>
    /// AGGRO: Chase the player who attacked us and peck when in range.
    /// </summary>
    private static void ProcessAggro(Entity enemy, Func<string, Entity?> findTarget)
    {
        enemy.AggroTicks++;

        // Find our aggro target
        var target = findTarget(enemy.AggroTargetId!);
        if (target == null || !target.IsAlive)
        {
            // Target gone — return to passive
            ClearAggro(enemy);
            return;
        }

        float dx = target.X - enemy.X;
        float dy = target.Y - enemy.Y;
        float distSq = dx * dx + dy * dy;

        if (distSq <= AttackRange * AttackRange)
        {
            // In range — attack!
            if (enemy.PrimaryFireCooldown <= 0)
            {
                target.TakeDamage(PeckDamage);
                target.IsDirty = true;
                enemy.PrimaryFireCooldown = AttackCooldownTicks;
                enemy.IsDirty = true;
            }

            // Stop moving while attacking
            enemy.VelocityX = 0;
            enemy.VelocityY = 0;
        }
        else
        {
            // Chase the target
            MoveToward(enemy, target.X, target.Y, ChaseSpeed);
        }
    }

    /// <summary>
    /// FLEE: Move away from the aggro target, then return to passive.
    /// </summary>
    private static void ProcessFlee(Entity enemy, Func<string, Entity?> findTarget)
    {
        enemy.AggroTicks++;

        // Check if flee duration is over
        if (enemy.AggroTicks >= FleeStartTick + FleeDurationTicks)
        {
            ClearAggro(enemy);
            return;
        }

        // Find the target to flee FROM
        var target = findTarget(enemy.AggroTargetId!);
        if (target == null)
        {
            ClearAggro(enemy);
            return;
        }

        // Move directly AWAY from the target
        float dx = enemy.X - target.X;
        float dy = enemy.Y - target.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);

        if (dist < 0.01f)
        {
            // Basically on top of target — pick random flee direction
            float randomAngle = Random.Shared.NextSingle() * MathF.PI * 2f;
            dx = MathF.Cos(randomAngle);
            dy = MathF.Sin(randomAngle);
            dist = 1f;
        }

        // Normalize and apply flee speed
        float fleeX = enemy.X + (dx / dist) * 5f; // Flee toward a point 5 tiles away
        float fleeY = enemy.Y + (dy / dist) * 5f;
        MoveToward(enemy, fleeX, fleeY, FleeSpeed);
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    /// <summary>
    /// Move an enemy toward a target position at the given speed.
    /// Sets velocity for smooth movement (processed by position update in tick loop).
    /// Stops if already at/near the target.
    /// </summary>
    private static void MoveToward(Entity enemy, float targetX, float targetY, float speed)
    {
        float dx = targetX - enemy.X;
        float dy = targetY - enemy.Y;
        float distSq = dx * dx + dy * dy;

        // Already at target — stop
        if (distSq < ArrivalThreshold * ArrivalThreshold)
        {
            enemy.VelocityX = 0;
            enemy.VelocityY = 0;
            return;
        }

        // Normalize direction and apply speed (converted to tiles/tick)
        float dist = MathF.Sqrt(distSq);
        float speedPerTick = speed / 20f; // Convert tiles/sec to tiles/tick at 20Hz

        enemy.VelocityX = (dx / dist) * speedPerTick;
        enemy.VelocityY = (dy / dist) * speedPerTick;

        // Apply movement directly (host-authoritative — no prediction needed)
        enemy.X += enemy.VelocityX;
        enemy.Y += enemy.VelocityY;
        enemy.IsDirty = true;
    }

    /// <summary>
    /// Clear all aggro state and return enemy to passive wandering.
    /// Also clears TaggedBy so the enemy can be re-tagged on next encounter.
    /// </summary>
    private static void ClearAggro(Entity enemy)
    {
        enemy.AggroTargetId = null;
        enemy.AggroTicks = 0;
        enemy.TaggedBy = null;
        enemy.VelocityX = 0;
        enemy.VelocityY = 0;
        enemy.IsDirty = true;
    }
}
