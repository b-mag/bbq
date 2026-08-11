// =============================================================================
// AISystem.cs — Enemy AI State Machine
// =============================================================================
//
// WHY STATE MACHINE (not behavior trees):
// A simple state machine (Idle/Patrol/Chase/Attack/Flee) is sufficient for
// enemies in a wave-defense game. Behavior trees add complexity that's only
// justified for enemies with many overlapping priorities. Our enemies have
// straightforward behavior: see player → chase → attack → flee if dying.
//
// WHY SEPARATE AIBrain OBJECTS:
// Entity.cs stays lean (just position/health/combat data). AI state (which player
// am I chasing, how long have I been patrolling, where am I heading) is stored
// in AIBrain objects keyed by entity ID. This separation means non-AI entities
// (players, projectiles) don't carry unused AI fields.
//
// PERFORMANCE:
// AI runs every tick but expensive operations (pathfinding) are rate-limited:
//   - Path recalculation: every 10 ticks (500ms)
//   - State transitions: based on tick counters and distance checks (very cheap)
//   - MaxSearchNodes=500 in pathfinding prevents runaway searches on complex maps
// =============================================================================

namespace Carcosa.Server.Game;

/// <summary>
/// AI states for enemy entities. Each state has distinct behavior and
/// transition conditions documented in AISystem.UpdateEnemy().
/// </summary>
public enum AIState
{
    Idle,
    Patrol,
    Chase,
    Attack,
    Flee
}

/// <summary>
/// AI behavior data attached to enemy entities.
/// Stored in a separate dictionary keyed by entity ID since Entity class stays lean.
/// </summary>
public sealed class AIBrain
{
    public AIState State { get; set; } = AIState.Idle;
    public string? TargetPlayerId { get; set; }
    public int StateTicks { get; set; } // Ticks spent in current state
    public int AttackCooldown { get; set; }
    public float PatrolTargetX { get; set; }
    public float PatrolTargetY { get; set; }
    public int PathRecalcCooldown { get; set; }
    public (float DirX, float DirY) MoveDirection { get; set; }
}

/// <summary>
/// AI system that updates all enemy entities each tick.
/// Implements state machine: Idle → Patrol → Chase → Attack → Flee.
/// </summary>
public sealed class AISystem
{
    private const float DetectionRange = 8f;
    private const float AttackRangeMelee = 1.5f;
    private const float AttackRangeRanged = 7f;
    private const float FleeHealthThreshold = 0.2f; // Flee below 20% HP
    private const int PathRecalcInterval = 10; // Recalculate path every 10 ticks
    private const float EnemySpeed = 2.5f; // Tiles per second (slower than players)

    private readonly Dictionary<string, AIBrain> _brains = new();
    private readonly Random _rng = new();

    /// <summary>
    /// Register a new enemy entity for AI processing.
    /// </summary>
    public void RegisterEnemy(Entity enemy)
    {
        _brains[enemy.Id] = new AIBrain
        {
            State = AIState.Idle,
            StateTicks = 0
        };
    }

    /// <summary>
    /// Remove an enemy from AI processing.
    /// </summary>
    public void UnregisterEnemy(string entityId)
    {
        _brains.Remove(entityId);
    }

    /// <summary>
    /// Update all enemy AI for one tick.
    /// </summary>
    public void Update(GameState state)
    {
        if (state.Map == null) return;

        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Enemy || !entity.IsAlive) continue;
            if (!_brains.TryGetValue(entity.Id, out var brain)) continue;

            brain.StateTicks++;
            if (brain.AttackCooldown > 0) brain.AttackCooldown--;

            UpdateEnemy(state, entity, brain);
        }
    }

    private void UpdateEnemy(GameState state, Entity enemy, AIBrain brain)
    {
        // Find nearest alive player
        Entity? nearestPlayer = null;
        float nearestDist = float.MaxValue;

        foreach (var player in state.GetAlivePlayers())
        {
            var dx = player.X - enemy.X;
            var dy = player.Y - enemy.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestPlayer = player;
            }
        }

        // State transitions
        switch (brain.State)
        {
            case AIState.Idle:
                if (nearestPlayer != null && nearestDist <= DetectionRange
                    && Pathfinding.HasLineOfSight(state.Map!, enemy.X, enemy.Y, nearestPlayer.X, nearestPlayer.Y))
                {
                    brain.State = AIState.Chase;
                    brain.TargetPlayerId = nearestPlayer.Id;
                    brain.StateTicks = 0;
                }
                else if (brain.StateTicks > 40) // Start patrolling after 2 seconds idle
                {
                    brain.State = AIState.Patrol;
                    brain.StateTicks = 0;
                    PickPatrolTarget(state, enemy, brain);
                }
                break;

            case AIState.Patrol:
                // Check for player detection
                if (nearestPlayer != null && nearestDist <= DetectionRange
                    && Pathfinding.HasLineOfSight(state.Map!, enemy.X, enemy.Y, nearestPlayer.X, nearestPlayer.Y))
                {
                    brain.State = AIState.Chase;
                    brain.TargetPlayerId = nearestPlayer.Id;
                    brain.StateTicks = 0;
                }
                else
                {
                    MoveToward(state, enemy, brain, brain.PatrolTargetX, brain.PatrolTargetY);

                    // Reached patrol target or timeout
                    var patrolDist = MathF.Sqrt(
                        MathF.Pow(enemy.X - brain.PatrolTargetX, 2) +
                        MathF.Pow(enemy.Y - brain.PatrolTargetY, 2));
                    if (patrolDist < 1f || brain.StateTicks > 100)
                    {
                        brain.State = AIState.Idle;
                        brain.StateTicks = 0;
                        enemy.VelocityX = 0;
                        enemy.VelocityY = 0;
                    }
                }
                break;

            case AIState.Chase:
                // Check if should flee
                if ((float)enemy.Health / enemy.MaxHealth < FleeHealthThreshold)
                {
                    brain.State = AIState.Flee;
                    brain.StateTicks = 0;
                    break;
                }

                if (nearestPlayer == null || !nearestPlayer.IsAlive)
                {
                    brain.State = AIState.Idle;
                    brain.StateTicks = 0;
                    enemy.VelocityX = 0;
                    enemy.VelocityY = 0;
                    break;
                }

                // Check if in attack range (ranged enemies attack from distance)
                var attackRange = IsRangedEnemy(enemy.SubType) ? AttackRangeRanged : AttackRangeMelee;
                if (nearestDist <= attackRange)
                {
                    brain.State = AIState.Attack;
                    brain.StateTicks = 0;
                    enemy.VelocityX = 0;
                    enemy.VelocityY = 0;
                }
                else
                {
                    MoveToward(state, enemy, brain, nearestPlayer.X, nearestPlayer.Y);
                }

                // Lost sight — return to idle after a while
                if (nearestDist > DetectionRange * 1.5f && brain.StateTicks > 60)
                {
                    brain.State = AIState.Idle;
                    brain.StateTicks = 0;
                    enemy.VelocityX = 0;
                    enemy.VelocityY = 0;
                }
                break;

            case AIState.Attack:
                if (nearestPlayer == null || !nearestPlayer.IsAlive)
                {
                    brain.State = AIState.Idle;
                    brain.StateTicks = 0;
                    break;
                }

                var atkRange = IsRangedEnemy(enemy.SubType) ? AttackRangeRanged : AttackRangeMelee;
                if (nearestDist > atkRange * 1.2f)
                {
                    brain.State = AIState.Chase;
                    brain.StateTicks = 0;
                    break;
                }

                // Perform attack
                if (brain.AttackCooldown <= 0)
                {
                    PerformAttack(state, enemy, nearestPlayer);
                    brain.AttackCooldown = GetAttackCooldown(enemy.SubType);
                }
                break;

            case AIState.Flee:
                if (nearestPlayer != null)
                {
                    // Move away from nearest player
                    var fleeX = enemy.X - (nearestPlayer.X - enemy.X);
                    var fleeY = enemy.Y - (nearestPlayer.Y - enemy.Y);
                    MoveToward(state, enemy, brain, fleeX, fleeY);
                }

                // Stop fleeing after 3 seconds or if health recovered
                if (brain.StateTicks > 60 || (float)enemy.Health / enemy.MaxHealth > 0.4f)
                {
                    brain.State = AIState.Idle;
                    brain.StateTicks = 0;
                    enemy.VelocityX = 0;
                    enemy.VelocityY = 0;
                }
                break;
        }
    }

    private void MoveToward(GameState state, Entity enemy, AIBrain brain, float targetX, float targetY)
    {
        brain.PathRecalcCooldown--;
        if (brain.PathRecalcCooldown <= 0)
        {
            brain.MoveDirection = Pathfinding.GetDirectionToward(
                state.Map!, enemy.X, enemy.Y, targetX, targetY);
            brain.PathRecalcCooldown = PathRecalcInterval;
        }

        var (dirX, dirY) = brain.MoveDirection;
        var speed = EnemySpeed * GameLoop.TickDuration;
        enemy.VelocityX = dirX * speed;
        enemy.VelocityY = dirY * speed;
        enemy.IsDirty = true;
    }

    private void PerformAttack(GameState state, Entity enemy, Entity target)
    {
        switch (enemy.SubType)
        {
            case "cultist_acolyte":
                // Melee attack — basic cultist rushes in and strikes
                target.TakeDamage(5);
                break;

            case "cultist_torch":
                // Melee + burn DoT — torch cultist sets player on fire
                // Deals 8 immediate damage. DoT is handled by marking the target.
                // (DoT tick damage applied by GameFlowSystem if we add a burn status later;
                //  for now we do burst 8 + 3 bonus = 11 total as a single hit.)
                target.TakeDamage(11);
                break;

            case "cultist_chanter":
                // Ranged: fire an eldritch bolt toward the player
                FireEnemyProjectile(state, enemy, target, "eldritch_bolt", 8, 10f, 0.4f);
                break;

            case "cultist_dagger":
                // Ranged: throw a fast dagger projectile
                FireEnemyProjectile(state, enemy, target, "dagger", 6, 8f, 0.5f);
                break;

            case "cultist_shotgun":
                // Ranged: fire a spread of 5 pellets (shotgun blast)
                var baseAngle = MathF.Atan2(target.Y - enemy.Y, target.X - enemy.X);
                for (int i = 0; i < 5; i++)
                {
                    var spreadOffset = (i - 2) * 0.15f; // ~±0.3 radians total spread
                    var pelletAngle = baseAngle + spreadOffset + (_rng.NextSingle() - 0.5f) * 0.1f;
                    var pelletId = $"eproj_{_rng.Next(100000)}";
                    var pellet = new Entity
                    {
                        Id = pelletId,
                        Type = EntityType.Projectile,
                        SubType = "shotgun_pellet",
                        X = enemy.X + MathF.Cos(pelletAngle) * 0.5f,
                        Y = enemy.Y + MathF.Sin(pelletAngle) * 0.5f,
                        VelocityX = MathF.Cos(pelletAngle) * 0.5f,
                        VelocityY = MathF.Sin(pelletAngle) * 0.5f,
                        Damage = 3,
                        Range = 6f,
                        SourceEntityId = enemy.Id,
                        IsAlive = true,
                        IsDirty = true,
                        Health = 1,
                        MaxHealth = 1
                    };
                    state.AddEntity(pellet);
                }
                break;

            case "cultist_lightning":
                // Ranged: fire a lightning bolt that passes through entities (hits multiple)
                // Lightning bolt has high range and damage but slow cooldown
                FireEnemyProjectile(state, enemy, target, "lightning_bolt", 12, 12f, 0.6f);
                // Note: the "passes through" behavior is handled in GameLoop.CheckCollisions
                // by NOT removing lightning_bolt projectiles on first hit
                break;

            case "cult_leader":
                // AoE damage to all nearby players within 2-tile radius
                foreach (var player in state.GetAlivePlayers())
                {
                    var dx = player.X - enemy.X;
                    var dy = player.Y - enemy.Y;
                    if (dx * dx + dy * dy <= 4f) // 2 tile radius
                    {
                        player.TakeDamage(10);
                    }
                }
                break;

            case "boss_warehouse":
                // Boss attack: AoE slam (15 dmg in 3-tile radius) + summon 2 minions
                foreach (var player in state.GetAlivePlayers())
                {
                    var bDx = player.X - enemy.X;
                    var bDy = player.Y - enemy.Y;
                    if (bDx * bDx + bDy * bDy <= 9f) // 3 tile radius
                    {
                        player.TakeDamage(15);
                    }
                }
                // Summon 2 torch cultist minions near the boss
                for (int i = 0; i < 2; i++)
                {
                    var spawnAngle = _rng.NextSingle() * MathF.PI * 2;
                    var minionId = $"enemy_minion_{_rng.Next(100000)}";
                    var minion = new Entity
                    {
                        Id = minionId,
                        Type = EntityType.Enemy,
                        SubType = "cultist_torch",
                        X = enemy.X + MathF.Cos(spawnAngle) * 2f,
                        Y = enemy.Y + MathF.Sin(spawnAngle) * 2f,
                        Health = 25,
                        MaxHealth = 25,
                        Speed = 3f,
                        IsAlive = true,
                        IsDirty = true
                    };
                    state.AddEntity(minion);
                    RegisterEnemy(minion);
                }
                break;
        }

        enemy.IsDirty = true;
    }

    /// <summary>
    /// Helper to create an enemy projectile aimed at a target player.
    /// Reduces code duplication for ranged enemy attacks.
    /// </summary>
    private void FireEnemyProjectile(GameState state, Entity enemy, Entity target,
        string subType, int damage, float range, float speed)
    {
        var angle = MathF.Atan2(target.Y - enemy.Y, target.X - enemy.X);
        var proj = new Entity
        {
            Id = $"eproj_{_rng.Next(100000)}",
            Type = EntityType.Projectile,
            SubType = subType,
            X = enemy.X + MathF.Cos(angle) * 0.5f,
            Y = enemy.Y + MathF.Sin(angle) * 0.5f,
            VelocityX = MathF.Cos(angle) * speed,
            VelocityY = MathF.Sin(angle) * speed,
            Damage = damage,
            Range = range,
            SourceEntityId = enemy.Id,
            IsAlive = true,
            IsDirty = true,
            Health = 1,
            MaxHealth = 1
        };
        state.AddEntity(proj);
    }

    /// <summary>
    /// Determine if an enemy type attacks from range (affects chase/attack distance).
    /// </summary>
    private static bool IsRangedEnemy(string? subType) => subType is
        "cultist_chanter" or "cultist_dagger" or "cultist_shotgun" or "cultist_lightning";

    private static int GetAttackCooldown(string? subType) => subType switch
    {
        "cultist_acolyte" => 20,    // 1s — basic melee, moderate speed
        "cultist_torch" => 16,      // 0.8s — slightly faster melee (aggressive)
        "cultist_chanter" => 30,    // 1.5s — ranged, moderate cooldown
        "cultist_dagger" => 14,     // 0.7s — fast throwing knives
        "cultist_shotgun" => 50,    // 2.5s — slow but devastating spread
        "cultist_lightning" => 60,  // 3s — very slow but high damage
        "cult_leader" => 40,        // 2s — AoE attack
        "boss_warehouse" => 80,     // 4s — boss slam + summon (powerful but slow)
        _ => 20
    };

    private void PickPatrolTarget(GameState state, Entity enemy, AIBrain brain)
    {
        // Pick a random walkable tile within 5 tiles
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var tx = enemy.X + (_rng.NextSingle() - 0.5f) * 10f;
            var ty = enemy.Y + (_rng.NextSingle() - 0.5f) * 10f;
            if (state.Map!.IsWalkableF(tx, ty))
            {
                brain.PatrolTargetX = tx;
                brain.PatrolTargetY = ty;
                return;
            }
        }
        brain.PatrolTargetX = enemy.X;
        brain.PatrolTargetY = enemy.Y;
    }
}
