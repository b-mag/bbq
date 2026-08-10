namespace Carcosa.Server.Game;

/// <summary>
/// Manages wave-based enemy spawning.
/// 5 waves of increasing difficulty, with a boss on wave 5.
/// </summary>
public sealed class WaveSystem
{
    private const int WaveCount = 5;
    private const int IntermissionTicks = 600; // 30 seconds between waves (20 * 30)
    private const int SpawnIntervalTicks = 20; // Spawn enemies over 1 second per batch

    private readonly AISystem _aiSystem;
    private readonly Random _rng = new();
    private int _spawnCounter;

    public int CurrentWave { get; private set; }
    public int EnemiesRemaining { get; private set; }
    public int IntermissionCountdown { get; private set; }
    public bool IsSpawning { get; private set; }
    public bool AllWavesComplete { get; private set; }
    public bool BossSpawned { get; private set; }

    private int _spawnQueueRemaining;
    private int _spawnTickCounter;

    public WaveSystem(AISystem aiSystem)
    {
        _aiSystem = aiSystem;
    }

    /// <summary>
    /// Start the first wave.
    /// </summary>
    public void StartWaves(GameState state)
    {
        CurrentWave = 0;
        AllWavesComplete = false;
        BossSpawned = false;
        BeginNextWave(state);
    }

    /// <summary>
    /// Update the wave system each tick.
    /// </summary>
    public void Update(GameState state)
    {
        if (AllWavesComplete) return;

        // Handle intermission countdown
        if (state.Phase == GamePhase.WaveIntermission)
        {
            IntermissionCountdown--;
            if (IntermissionCountdown <= 0)
            {
                BeginNextWave(state);
            }
            return;
        }

        // Handle spawning enemies over time
        if (_spawnQueueRemaining > 0)
        {
            _spawnTickCounter++;
            if (_spawnTickCounter >= SpawnIntervalTicks)
            {
                _spawnTickCounter = 0;
                SpawnEnemy(state);
                _spawnQueueRemaining--;
            }
        }

        // Check if all enemies are dead
        EnemiesRemaining = 0;
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type == EntityType.Enemy && entity.IsAlive)
                EnemiesRemaining++;
        }

        if (EnemiesRemaining == 0 && _spawnQueueRemaining == 0 && CurrentWave > 0)
        {
            if (CurrentWave >= WaveCount)
            {
                AllWavesComplete = true;
            }
            else
            {
                // Start intermission
                state.Phase = GamePhase.WaveIntermission;
                IntermissionCountdown = IntermissionTicks;
            }
        }
    }

    private void BeginNextWave(GameState state)
    {
        CurrentWave++;
        state.CurrentWave = CurrentWave;
        state.Phase = GamePhase.Playing;
        IsSpawning = true;

        // Calculate enemies for this wave
        var enemyCount = GetEnemyCount(CurrentWave);
        _spawnQueueRemaining = enemyCount;
        _spawnTickCounter = 0;

        Console.WriteLine($"[Wave] Wave {CurrentWave} starting ({enemyCount} enemies)");

        // Spawn initial batch immediately
        var initialBatch = Math.Min(5, _spawnQueueRemaining);
        for (int i = 0; i < initialBatch; i++)
        {
            SpawnEnemy(state);
            _spawnQueueRemaining--;
        }
    }

    private void SpawnEnemy(GameState state)
    {
        if (state.Map == null) return;

        // Pick a spawn point
        var spawnPoints = state.Map.SpawnPoints;
        if (spawnPoints.Length == 0) return;

        var spawnPoint = spawnPoints[_rng.Next(spawnPoints.Length)];

        // Determine enemy type based on wave
        var (subType, health) = GetEnemyType(CurrentWave);

        // Create enemy entity
        var enemyId = $"enemy_{Interlocked.Increment(ref _spawnCounter)}";
        var enemy = new Entity
        {
            Id = enemyId,
            Type = EntityType.Enemy,
            SubType = subType,
            X = spawnPoint.X + (_rng.NextSingle() - 0.5f) * 2f,
            Y = spawnPoint.Y + (_rng.NextSingle() - 0.5f) * 2f,
            Health = health,
            MaxHealth = health,
            Speed = 2.5f,
            IsAlive = true,
            IsDirty = true
        };

        state.AddEntity(enemy);
        _aiSystem.RegisterEnemy(enemy);
    }

    /// <summary>
    /// Spawn the Herald of Hastur (final boss).
    /// Called during wave 5.
    /// </summary>
    public void SpawnBoss(GameState state)
    {
        if (state.Map == null || BossSpawned) return;
        BossSpawned = true;

        var spawnPoints = state.Map.SpawnPoints;
        var spawnPoint = spawnPoints.Length > 0
            ? spawnPoints[_rng.Next(spawnPoints.Length)]
            : new SpawnPoint(state.Map.Width / 2, state.Map.Height / 2, SpawnPointType.Street);

        var boss = new Entity
        {
            Id = "enemy_herald",
            Type = EntityType.Enemy,
            SubType = "cult_leader",
            X = spawnPoint.X,
            Y = spawnPoint.Y,
            Health = 500,
            MaxHealth = 500,
            Speed = 1.5f, // Slow but menacing
            IsAlive = true,
            IsDirty = true
        };

        state.AddEntity(boss);
        _aiSystem.RegisterEnemy(boss);
        Console.WriteLine("[Wave] The Herald of Hastur has arrived!");
    }

    private int GetEnemyCount(int wave) => wave switch
    {
        1 => 8,
        2 => 12,
        3 => 18,
        4 => 24,
        5 => 15, // Fewer but harder + boss
        _ => 10
    };

    private (string SubType, int Health) GetEnemyType(int wave)
    {
        var roll = _rng.NextSingle();

        return wave switch
        {
            1 => ("cultist_acolyte", 30), // Wave 1: all acolytes
            2 => roll < 0.7f ? ("cultist_acolyte", 30) : ("cultist_chanter", 50),
            3 => roll < 0.5f ? ("cultist_acolyte", 35) : ("cultist_chanter", 55),
            4 => roll < 0.4f ? ("cultist_acolyte", 40) : roll < 0.8f ? ("cultist_chanter", 60) : ("cult_leader", 150),
            5 => roll < 0.3f ? ("cultist_acolyte", 45) : roll < 0.7f ? ("cultist_chanter", 65) : ("cult_leader", 150),
            _ => ("cultist_acolyte", 30)
        };
    }
}
