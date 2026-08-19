// =============================================================================
// WaveSystem.cs — Dungeon encounter packing
// =============================================================================
//
// Default style is Fixed (C): spawn trash, elites, and one boss once. Dead
// enemies do not come back. Waves / continuous respawn stay as later styles
// (coliseum / capture arena).
// =============================================================================

using Carcosa.Server.Gameplay;

namespace Carcosa.Server.Game;

/// <summary>
/// Populates a dungeon encounter. Default style is Fixed (no respawn).
/// </summary>
public sealed class WaveSystem
{
    private readonly AISystem _aiSystem;
    private readonly Random _rng = new();
    private int _spawnCounter;

    public int CurrentWave { get; private set; } = 1;
    public int EnemiesRemaining { get; private set; }
    public int IntermissionCountdown { get; private set; }
    public bool IsSpawning { get; private set; }
    public bool AllWavesComplete { get; private set; }
    public bool BossSpawned { get; private set; }
    public DungeonSpawnStyle Style { get; private set; } = DungeonSpawnStyle.Fixed;

    public WaveSystem(AISystem aiSystem)
    {
        _aiSystem = aiSystem;
    }

    /// <summary>Pack the instance with a fixed enemy set (style C) plus one boss.</summary>
    public void StartWaves(GameState state)
        => StartEncounter(state, DungeonRules.DefaultSpawnStyle);

    public void StartEncounter(GameState state, DungeonSpawnStyle style)
    {
        Style = style;
        AllWavesComplete = false;
        BossSpawned = false;
        CurrentWave = 1;
        state.CurrentWave = 1;
        state.Phase = GamePhase.Playing;
        IsSpawning = true;

        PackFixed(state);
        IsSpawning = false;
        CountAlive(state);
        Console.WriteLine($"[Dungeon] Packed {EnemiesRemaining} enemies (style={style}, level={state.AvgLevel})");
    }

    public void Update(GameState state)
    {
        if (AllWavesComplete) return;
        CountAlive(state);
        if (EnemiesRemaining == 0)
            AllWavesComplete = true;
    }

    public void SpawnBoss(GameState state)
        => SpawnBossInternal(state);

    private void PackFixed(GameState state)
    {
        var trash = DungeonRules.TrashCount(state.AvgLevel);
        var elites = DungeonRules.EliteCount(state.AvgLevel);
        for (int i = 0; i < trash; i++)
        {
            var (subType, health) = RollTrash(state.AvgLevel);
            SpawnAt(state, subType, health);
        }

        for (int i = 0; i < elites; i++)
        {
            var (subType, health) = RollTrash(state.AvgLevel);
            SpawnAt(state, "elite_" + subType, DungeonRules.ScaleStat(health * 3, state.AvgLevel), alreadyScaled: true);
        }

        SpawnBossInternal(state);
    }

    private void CountAlive(GameState state)
    {
        EnemiesRemaining = 0;
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type == EntityType.Enemy && entity.IsAlive)
                EnemiesRemaining++;
        }
    }

    private void SpawnAt(GameState state, string subType, int health, bool alreadyScaled = false)
    {
        if (state.Map == null) return;
        var spawnPoint = DungeonRules.PickEnemySpawn(state.Map, _rng);
        if (spawnPoint == null) return;

        if (!alreadyScaled)
            health = DungeonRules.ScaleStat(health, state.AvgLevel);

        var x = spawnPoint.X + (_rng.NextSingle() - 0.5f) * 2f;
        var y = spawnPoint.Y + (_rng.NextSingle() - 0.5f) * 2f;
        if (DungeonRules.IsNearEntrance(x, y, state.Map))
        {
            x = spawnPoint.X + 0.5f;
            y = spawnPoint.Y + 0.5f;
        }

        var enemy = new Entity
        {
            Id = $"enemy_{Interlocked.Increment(ref _spawnCounter)}",
            Type = EntityType.Enemy,
            SubType = subType,
            X = x,
            Y = y,
            SpawnX = x,
            SpawnY = y,
            Health = health,
            MaxHealth = health,
            Speed = 2.5f,
            IsAlive = true,
            IsDirty = true
        };
        state.AddEntity(enemy);
        _aiSystem.RegisterEnemy(enemy);
    }

    private void SpawnBossInternal(GameState state)
    {
        if (state.Map == null || BossSpawned) return;
        BossSpawned = true;

        var spawnPoint = DungeonRules.PickFarthestEnemySpawn(state.Map)
            ?? DungeonRules.PickEnemySpawn(state.Map, _rng)
            ?? new SpawnPoint(state.Map.Width / 2, Math.Max(2, state.Map.Height / 4), SpawnPointType.Street);
        var bossHp = DungeonRules.ScaleStat(500, state.AvgLevel);

        var boss = new Entity
        {
            Id = "enemy_boss",
            Type = EntityType.Enemy,
            SubType = "boss_warehouse",
            X = spawnPoint.X + 0.5f,
            Y = spawnPoint.Y + 0.5f,
            SpawnX = spawnPoint.X + 0.5f,
            SpawnY = spawnPoint.Y + 0.5f,
            Health = bossHp,
            MaxHealth = bossHp,
            Speed = 1.5f,
            IsAlive = true,
            IsDirty = true
        };
        state.AddEntity(boss);
        _aiSystem.RegisterEnemy(boss);
    }

    private (string SubType, int Health) RollTrash(int dungeonLevel)
    {
        var roll = _rng.NextSingle();
        var meleeOnly = !DungeonRules.AllowsEnemyProjectiles(dungeonLevel);
        var level = DungeonRules.ClampLevel(dungeonLevel);

        if (meleeOnly)
            return roll < 0.55f ? ("cultist_torch", 20) : ("cultist_acolyte", 18);

        if (level <= 12)
            return roll < 0.5f ? ("cultist_torch", 22) : ("cultist_dagger", 18);

        if (level <= 24)
        {
            if (roll < 0.35f) return ("cultist_torch", 30);
            if (roll < 0.65f) return ("cultist_dagger", 26);
            return ("cultist_shotgun", 40);
        }

        if (roll < 0.25f) return ("cultist_torch", 40);
        if (roll < 0.45f) return ("cultist_shotgun", 50);
        if (roll < 0.65f) return ("cultist_lightning", 45);
        if (roll < 0.85f) return ("cultist_dagger", 35);
        return ("cult_leader", 120);
    }
}
