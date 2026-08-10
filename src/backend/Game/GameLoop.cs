using System.Diagnostics;
using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// The core server game loop running at a fixed tick rate (20 ticks/sec = 50ms per tick).
/// Processes player inputs, updates entity positions, handles collisions, and broadcasts state.
/// Runs on a dedicated thread to avoid blocking the HTTP/WebSocket handler threads.
/// </summary>
public sealed class GameLoop : IDisposable
{
    public const int TickRate = 20; // ticks per second
    public const float TickDuration = 1f / TickRate; // seconds per tick (0.05s)
    public const float PlayerSpeed = 5f; // tiles per second

    private readonly GameState _state;
    private readonly InputQueue _inputQueue;
    private readonly ConnectionManager _connectionManager;
    private readonly AISystem _aiSystem;
    private readonly WaveSystem _waveSystem;
    private readonly GameFlowSystem _gameFlowSystem;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    private volatile bool _isRunning;

    public GameState State => _state;
    public InputQueue InputQueue => _inputQueue;
    public AISystem AI => _aiSystem;
    public WaveSystem Waves => _waveSystem;
    public GameFlowSystem Flow => _gameFlowSystem;
    public bool IsRunning => _isRunning;

    // SessionManager reference (set after construction)
    public SessionManager? Session { get; set; }

    public GameLoop(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
        _state = new GameState();
        _inputQueue = new InputQueue();
        _aiSystem = new AISystem();
        _waveSystem = new WaveSystem(_aiSystem);
        _gameFlowSystem = new GameFlowSystem(connectionManager);

        _thread = new Thread(RunLoop)
        {
            IsBackground = true,
            Name = "GameLoop",
            Priority = ThreadPriority.AboveNormal
        };
    }

    /// <summary>
    /// Start the game loop on its dedicated thread.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _thread.Start();
        Console.WriteLine("[GameLoop] Started at {0} ticks/sec", TickRate);
    }

    /// <summary>
    /// Stop the game loop gracefully.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        Console.WriteLine("[GameLoop] Stopped");
    }

    /// <summary>
    /// Add a player entity to the game state.
    /// </summary>
    public Entity AddPlayer(string playerId, string playerName, string playerClass, float spawnX, float spawnY)
    {
        var entity = new Entity
        {
            Id = $"player_{playerId}",
            Type = EntityType.Player,
            SubType = playerClass,
            OwnerId = playerId,
            X = spawnX,
            Y = spawnY,
            Health = 100,
            MaxHealth = 100,
            Speed = PlayerSpeed,
            IsAlive = true,
            IsDirty = true
        };

        _state.AddEntity(entity);
        return entity;
    }

    /// <summary>
    /// Remove a player entity from the game state.
    /// </summary>
    public void RemovePlayer(string playerId)
    {
        var entity = _state.GetPlayerByOwnerId(playerId);
        if (entity != null)
        {
            _state.RemoveEntity(entity.Id);
        }
    }

    private void RunLoop()
    {
        var stopwatch = Stopwatch.StartNew();
        var targetTickMs = 1000.0 / TickRate; // 50ms
        var nextTickTime = stopwatch.Elapsed.TotalMilliseconds;

        while (_isRunning && !_cts.Token.IsCancellationRequested)
        {
            var currentTime = stopwatch.Elapsed.TotalMilliseconds;

            if (currentTime >= nextTickTime)
            {
                Tick();
                nextTickTime += targetTickMs;

                // If we've fallen behind, catch up but don't spiral
                if (currentTime - nextTickTime > targetTickMs * 5)
                {
                    nextTickTime = currentTime + targetTickMs;
                }
            }
            else
            {
                // Sleep briefly to avoid busy-waiting, but stay responsive
                var sleepMs = (int)(nextTickTime - currentTime);
                if (sleepMs > 1)
                {
                    Thread.Sleep(sleepMs - 1);
                }
                else
                {
                    Thread.SpinWait(100);
                }
            }
        }
    }

    private void Tick()
    {
        _state.Tick++;

        // 1. Process all queued player inputs
        ProcessInputs();

        // 2. Update entity positions (velocity-based movement)
        UpdatePositions();

        // 3. Update projectiles
        UpdateProjectiles();

        // 4. Check collisions (projectiles vs entities, entities vs walls)
        CheckCollisions();

        // 5. Update AI
        if (_state.Phase == GamePhase.Playing)
        {
            _aiSystem.Update(_state);
            _waveSystem.Update(_state);

            // Spawn boss on wave 5 when half the regular enemies are dead
            if (_waveSystem.CurrentWave == 5 && !_waveSystem.BossSpawned
                && _waveSystem.EnemiesRemaining < 8)
            {
                _waveSystem.SpawnBoss(_state);
            }

            // Check for victory (all waves complete + all enemies dead)
            if (_waveSystem.AllWavesComplete && _waveSystem.EnemiesRemaining == 0 && Session != null)
            {
                Session.EndGame(victory: true);
                _ = _connectionManager.BroadcastAsync(new GameMessage
                {
                    Type = MessageTypes.GameEvent,
                    GameEvent = new GameEventPayload
                    {
                        Event = "victory",
                        Message = "The Herald falls! Carcosa's grip weakens... for now."
                    }
                });
            }
        }

        // 6. Game flow (death/revive/game-over checks)
        if (Session != null)
        {
            _gameFlowSystem.Update(_state, Session);
        }

        // 6. Update cooldowns
        UpdateCooldowns();

        // 7. Broadcast state to all connected clients
        BroadcastState();
    }

    private void ProcessInputs()
    {
        var inputs = _inputQueue.DrainAll();

        foreach (var entry in inputs)
        {
            var entity = _state.GetPlayerByOwnerId(entry.PlayerId);
            if (entity == null || !entity.IsAlive) continue;

            var input = entry.Input;

            // Normalize diagonal movement
            var moveX = input.MoveX;
            var moveY = input.MoveY;
            var magnitude = MathF.Sqrt(moveX * moveX + moveY * moveY);
            if (magnitude > 1f)
            {
                moveX /= magnitude;
                moveY /= magnitude;
            }

            // Convert to velocity (tiles per tick)
            entity.VelocityX = moveX * entity.Speed * TickDuration;
            entity.VelocityY = moveY * entity.Speed * TickDuration;

            // Process combat actions
            if (input.PrimaryFire)
            {
                CombatSystem.ProcessPrimaryFire(_state, entity, input.AimAngle);
            }
            if (input.SecondaryAbility)
            {
                CombatSystem.ProcessSecondaryAbility(_state, entity);
            }
            if (input.Interact)
            {
                _gameFlowSystem.ProcessReviveInteraction(_state, entity);
            }

            // Track the last processed input sequence for client reconciliation
            entity.LastProcessedInput = input.SequenceNumber;
            entity.IsDirty = true;
        }
    }

    private void UpdatePositions()
    {
        foreach (var (_, entity) in _state.Entities)
        {
            if (!entity.IsAlive) continue;
            if (entity.VelocityX == 0 && entity.VelocityY == 0) continue;

            var newX = entity.X + entity.VelocityX;
            var newY = entity.Y + entity.VelocityY;

            // Collision check against map
            if (_state.Map != null)
            {
                // Check X movement
                if (!_state.Map.IsWalkableF(newX, entity.Y))
                {
                    newX = entity.X;
                }
                // Check Y movement
                if (!_state.Map.IsWalkableF(entity.X, newY))
                {
                    newY = entity.Y;
                }
                // Check diagonal
                if (newX != entity.X && newY != entity.Y && !_state.Map.IsWalkableF(newX, newY))
                {
                    // Allow sliding along walls
                    if (_state.Map.IsWalkableF(newX, entity.Y))
                    {
                        newY = entity.Y;
                    }
                    else if (_state.Map.IsWalkableF(entity.X, newY))
                    {
                        newX = entity.X;
                    }
                    else
                    {
                        newX = entity.X;
                        newY = entity.Y;
                    }
                }
            }

            if (newX != entity.X || newY != entity.Y)
            {
                entity.X = newX;
                entity.Y = newY;
                entity.IsDirty = true;
            }

            // Players stop when no input (friction)
            if (entity.Type == EntityType.Player)
            {
                entity.VelocityX = 0;
                entity.VelocityY = 0;
            }
        }
    }

    private void UpdateProjectiles()
    {
        var toRemove = new List<string>();

        foreach (var projectile in _state.GetProjectiles())
        {
            // Move projectile
            projectile.X += projectile.VelocityX;
            projectile.Y += projectile.VelocityY;
            projectile.DistanceTraveled += MathF.Sqrt(
                projectile.VelocityX * projectile.VelocityX +
                projectile.VelocityY * projectile.VelocityY);
            projectile.IsDirty = true;

            // Check if out of range
            if (projectile.DistanceTraveled >= projectile.Range)
            {
                toRemove.Add(projectile.Id);
                continue;
            }

            // Check wall collision
            if (_state.Map != null && !_state.Map.IsWalkableF(projectile.X, projectile.Y))
            {
                toRemove.Add(projectile.Id);
            }
        }

        foreach (var id in toRemove)
        {
            _state.RemoveEntity(id);
        }
    }

    private void CheckCollisions()
    {
        // Projectile vs Entity collisions
        var projectilesToRemove = new List<string>();

        foreach (var projectile in _state.GetProjectiles())
        {
            foreach (var (_, target) in _state.Entities)
            {
                if (!target.IsAlive) continue;
                if (target.Id == projectile.SourceEntityId) continue; // Don't hit self
                if (target.Type == EntityType.Projectile) continue; // Projectiles don't hit each other

                // Determine if projectile should hit this entity type
                // Player projectiles hit enemies, enemy projectiles hit players
                var sourceEntity = _state.Entities.GetValueOrDefault(projectile.SourceEntityId ?? "");
                if (sourceEntity?.Type == EntityType.Player && target.Type != EntityType.Enemy) continue;
                if (sourceEntity?.Type == EntityType.Enemy && target.Type != EntityType.Player) continue;

                // Simple circle collision (0.4 tile radius for entities)
                var dx = projectile.X - target.X;
                var dy = projectile.Y - target.Y;
                var distSq = dx * dx + dy * dy;
                const float hitRadius = 0.4f;

                if (distSq <= hitRadius * hitRadius)
                {
                    target.TakeDamage(projectile.Damage);
                    projectilesToRemove.Add(projectile.Id);
                    break; // Projectile hits one target
                }
            }
        }

        foreach (var id in projectilesToRemove)
        {
            _state.RemoveEntity(id);
        }
    }

    private void UpdateCooldowns()
    {
        foreach (var (_, entity) in _state.Entities)
        {
            if (entity.PrimaryFireCooldown > 0)
                entity.PrimaryFireCooldown--;
            if (entity.SecondaryAbilityCooldown > 0)
                entity.SecondaryAbilityCooldown--;
        }
    }

    private void BroadcastState()
    {
        // Only broadcast if there are connected players and dirty entities
        if (_connectionManager.ConnectionCount == 0) return;

        var dirtyEntities = _state.GetDirtyEntities().ToArray();
        if (dirtyEntities.Length == 0) return;

        // Build entity state array for the message
        var entityStates = new EntityState[dirtyEntities.Length];
        for (int i = 0; i < dirtyEntities.Length; i++)
        {
            var e = dirtyEntities[i];
            entityStates[i] = new EntityState
            {
                Id = e.Id,
                EntityType = e.Type switch
                {
                    EntityType.Player => "player",
                    EntityType.Enemy => "enemy",
                    EntityType.Projectile => "projectile",
                    _ => "unknown"
                },
                X = e.X,
                Y = e.Y,
                VelocityX = e.VelocityX,
                VelocityY = e.VelocityY,
                Health = e.Health,
                MaxHealth = e.MaxHealth,
                SubType = e.SubType,
                IsAlive = e.IsAlive
            };
        }

        // Send personalized state to each player (includes their lastProcessedInput)
        foreach (var playerId in _connectionManager.GetConnectedPlayerIds())
        {
            var playerEntity = _state.GetPlayerByOwnerId(playerId);
            var message = new GameMessage
            {
                Type = MessageTypes.GameState,
                GameState = new GameStatePayload
                {
                    Tick = _state.Tick,
                    Entities = entityStates,
                    LastProcessedInput = playerEntity?.LastProcessedInput
                }
            };

            // Fire-and-forget send (don't block the game loop)
            _ = _connectionManager.SendToAsync(playerId, message);
        }

        _state.ClearDirtyFlags();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }
}
