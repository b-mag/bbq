// =============================================================================
// GameLoop.cs — Core Server Game Loop (20Hz Fixed Timestep)
// =============================================================================
//
// WHY A DEDICATED THREAD:
// The game loop must run at a consistent 20Hz regardless of how many HTTP requests
// or WebSocket messages are being processed. Running on the thread pool would mean
// the game update competes with I/O work for thread time, causing inconsistent tick
// timing. A dedicated thread with AboveNormal priority ensures the game loop always
// gets CPU time promptly.
//
// WHY 20Hz (not 60Hz or 128Hz):
// 20 ticks/second is the sweet spot for a cooperative top-down RPG:
//   - Fast enough for responsive combat (50ms between updates)
//   - Slow enough to minimize bandwidth (each tick broadcasts state to all players)
//   - Client renders at 60fps using interpolation between ticks for smooth visuals
//   - Matches common game server standards (Minecraft=20Hz, Overwatch=63Hz, CS2=128Hz)
//   - At 8 players, 20Hz = 160 messages/sec total — easily handled by WebSocket
//
// TICK PIPELINE:
// Each tick processes in strict order to ensure deterministic behavior:
//   1. Process inputs (player movement/actions for this tick)
//   2. Update positions (apply velocities with collision detection)
//   3. Update projectiles (move bullets, check range limits)
//   4. Check collisions (projectile-entity hits)
//   5. Update AI (enemy state machines decide next action)
//   6. Game flow (death detection, game-over checks)
//   7. Update cooldowns (decrement timers)
//   8. Broadcast state (send delta updates to all clients)
//
// WHY THIS ORDER MATTERS:
// Inputs must be processed before positions so movement applies this tick.
// Positions before collisions so hits are checked at new positions.
// AI after collisions so enemies react to damage taken this tick.
// Broadcast last so clients get the final state for this tick.
// =============================================================================

using System.Diagnostics;
using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// The core server game loop running at a fixed tick rate (20 ticks/sec = 50ms per tick).
/// Processes player inputs, updates entity positions, handles collisions, and broadcasts state.
/// Runs on a dedicated thread to avoid blocking the HTTP/WebSocket handler threads.
/// 
/// WHY IDisposable: The background thread and CancellationTokenSource must be
/// cleaned up on server shutdown to prevent the process from hanging.
/// </summary>
public sealed class GameLoop : IDisposable
{
    /// <summary>Server tick rate: 20 updates per second.</summary>
    public const int TickRate = 20;
    /// <summary>Duration of one tick in seconds (0.05s). Used for velocity calculations.</summary>
    public const float TickDuration = 1f / TickRate;
    /// <summary>Player movement speed in tiles per second. Converted to tiles/tick in ProcessInputs.</summary>
    public const float PlayerSpeed = 5f;

    private readonly GameState _state;
    private readonly InputQueue _inputQueue;
    private readonly ConnectionManager _connectionManager;
    private readonly AISystem _aiSystem;
    private readonly WaveSystem _waveSystem;
    private readonly GameFlowSystem _gameFlowSystem;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Volatile because it's read from the HTTP thread (health check endpoint)
    /// and written from the game loop thread. Volatile ensures visibility across threads.
    /// </summary>
    private volatile bool _isRunning;

    public GameState State => _state;
    public InputQueue InputQueue => _inputQueue;
    public AISystem AI => _aiSystem;
    public WaveSystem Waves => _waveSystem;
    public GameFlowSystem Flow => _gameFlowSystem;
    public bool IsRunning => _isRunning;

    /// <summary>
    /// SessionManager reference — set after construction to break circular dependency.
    /// The GameLoop needs Session to call EndGame(), and SessionManager needs GameLoop
    /// to access State. Both are singletons resolved from DI.
    /// </summary>
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
            IsBackground = true, // Won't prevent process exit
            Name = "GameLoop",
            Priority = ThreadPriority.AboveNormal // Prioritize game updates over I/O
        };
    }

    /// <summary>
    /// Start the game loop on its dedicated thread.
    /// Called once during server startup. Idempotent (second call is a no-op).
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _thread.Start();
        Console.WriteLine("[GameLoop] Started at {0} ticks/sec", TickRate);
    }

    /// <summary>
    /// Stop the game loop gracefully. Signals the thread to exit and waits up to 2 seconds.
    /// </summary>
    public void Stop()
    {
        _isRunning = false;
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(2));
        Console.WriteLine("[GameLoop] Stopped");
    }

    /// <summary>
    /// Add a player entity to the game state with class-appropriate stats.
    /// Called by SessionManager when the game starts.
    /// </summary>
    public Entity AddPlayer(string playerId, string playerName, string playerClass, float spawnX, float spawnY)
    {
        // Med kit starting counts vary by class:
        // Detective=3 (investigator, prepared), Gangster=1 (tough but not a medic), Surgeon=0 (IS the medic)
        var medKits = playerClass switch
        {
            "detective" => 3,
            "gangster" => 1,
            "surgeon" => 0,
            _ => 1
        };

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
            IsDirty = true,
            MedKits = medKits
        };

        _state.AddEntity(entity);
        return entity;
    }

    /// <summary>
    /// Remove a player entity (on disconnect or return to lobby).
    /// </summary>
    public void RemovePlayer(string playerId)
    {
        var entity = _state.GetPlayerByOwnerId(playerId);
        if (entity != null)
        {
            _state.RemoveEntity(entity.Id);
        }
    }

    /// <summary>
    /// The main loop method running on the dedicated thread.
    /// 
    /// WHY Stopwatch OVER DateTime: Stopwatch uses the OS high-resolution timer
    /// (QueryPerformanceCounter on Windows) which has sub-millisecond precision.
    /// DateTime.UtcNow has ~15ms resolution on Windows — too coarse for 50ms ticks.
    /// 
    /// WHY SLEEP + SPIN-WAIT HYBRID: 
    /// - Thread.Sleep(N) releases the CPU but has ~1-2ms granularity
    /// - SpinWait burns CPU but gives precise timing
    /// We sleep when far from the next tick (saves power/CPU), then spin-wait
    /// for the final millisecond to hit precise timing.
    /// 
    /// WHY CATCH-UP LOGIC: If the tick falls behind (e.g., GC pause, OS scheduler),
    /// we skip ahead rather than running ticks as fast as possible to catch up.
    /// Running 100 ticks at once would spike CPU and produce a burst of network traffic.
    /// </summary>
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

                // If we've fallen behind by more than 5 ticks (250ms), reset.
                // This prevents a "death spiral" where catching up causes more lag.
                if (currentTime - nextTickTime > targetTickMs * 5)
                {
                    nextTickTime = currentTime + targetTickMs;
                }
            }
            else
            {
                // Wait for next tick time
                var sleepMs = (int)(nextTickTime - currentTime);
                if (sleepMs > 1)
                {
                    // Sleep releases CPU — use when we have time to spare
                    Thread.Sleep(sleepMs - 1);
                }
                else
                {
                    // Spin-wait for sub-millisecond precision on the final approach
                    Thread.SpinWait(100);
                }
            }
        }
    }

    /// <summary>
    /// Execute one complete game tick. Called exactly 20 times per second.
    /// Each step in the pipeline is documented with its purpose.
    /// </summary>
    private void Tick()
    {
        _state.Tick++;

        // 1. PROCESS INPUTS: Convert queued player inputs into entity velocities and actions.
        //    Must happen first so movement/combat applies this tick.
        ProcessInputs();

        // 2. UPDATE POSITIONS: Apply velocities to all entities with wall collision detection.
        //    Entities slide along walls rather than stopping completely (feels better).
        UpdatePositions();

        // 3. UPDATE PROJECTILES: Move bullets/bolts forward, remove if out of range or hitting walls.
        UpdateProjectiles();

        // 4. CHECK COLLISIONS: Test projectiles against entities (circle-circle collision).
        //    Player projectiles only hit enemies; enemy projectiles only hit players.
        CheckCollisions();

        // 5. UPDATE AI: Only during active gameplay (not lobby/intermission).
        //    AI state machines decide enemy movement and attacks.
        //    Wave system handles spawning and wave progression.
        if (_state.Phase == GamePhase.Playing)
        {
            _aiSystem.Update(_state);
            _waveSystem.Update(_state);

            // Boss spawn trigger: wave 5, when most regular enemies are dead
            if (_waveSystem.CurrentWave == 5 && !_waveSystem.BossSpawned
                && _waveSystem.EnemiesRemaining < 8)
            {
                _waveSystem.SpawnBoss(_state);
            }

            // Victory condition: all waves complete AND no enemies remain
            if (_waveSystem.AllWavesComplete && _waveSystem.EnemiesRemaining == 0 && Session != null)
            {
                Session.EndGame(victory: true);
                _ = _connectionManager.BroadcastAsync(new GameMessage
                {
                    Type = MessageTypes.GameEvent,
                    GameEvent = new GameEventPayload
                    {
                        Event = "victory",
                        Message = "Victory! The darkness recedes... for now."
                    }
                });
            }
        }

        // 6. GAME FLOW: Check for all-players-dead (game over) and handle revive progress.
        if (Session != null)
        {
            _gameFlowSystem.Update(_state, Session);
        }

        // 7. UPDATE COOLDOWNS: Decrement weapon/ability cooldown timers for all entities.
        UpdateCooldowns();

        // 8. BROADCAST STATE: Send delta updates (only dirty entities) to all clients.
        //    Each player gets a personalized message with their lastProcessedInput.
        BroadcastState();
    }

    /// <summary>
    /// Process all queued player inputs for this tick.
    /// Converts movement input into velocity and triggers combat actions.
    /// </summary>
    private void ProcessInputs()
    {
        var inputs = _inputQueue.DrainAll();

        foreach (var entry in inputs)
        {
            var entity = _state.GetPlayerByOwnerId(entry.PlayerId);
            if (entity == null || !entity.IsAlive) continue;

            var input = entry.Input;

            // WHY NORMALIZE: If the client sends diagonal input (moveX=1, moveY=1),
            // the magnitude would be ~1.41, making diagonal movement 41% faster.
            // Normalizing ensures consistent speed in all directions.
            var moveX = input.MoveX;
            var moveY = input.MoveY;
            var magnitude = MathF.Sqrt(moveX * moveX + moveY * moveY);
            if (magnitude > 1f)
            {
                moveX /= magnitude;
                moveY /= magnitude;
            }

            // Convert movement input to velocity (tiles per tick, not tiles per second)
            entity.VelocityX = moveX * entity.Speed * TickDuration;
            entity.VelocityY = moveY * entity.Speed * TickDuration;

            // Process combat actions (fire, ability, interact)
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
            // Process med kit use (H key)
            if (input.UseMedKit && entity.MedKits > 0 && entity.Health < entity.MaxHealth)
            {
                entity.MedKits--;
                entity.Health = entity.MaxHealth; // Full heal
                entity.IsDirty = true;
                Console.WriteLine($"[Game] Player {entity.Id} used a med kit ({entity.MedKits} remaining)");
            }

            // WHY TRACK LastProcessedInput: The client sends inputs with sequence numbers.
            // By echoing back the last one we processed, the client knows which predictions
            // have been confirmed and can discard them from its replay buffer.
            entity.LastProcessedInput = input.SequenceNumber;
            entity.IsDirty = true;
        }
    }

    /// <summary>
    /// Apply velocity to all entities with wall collision detection.
    /// Uses "try X, try Y, try both" pattern for smooth wall sliding.
    /// </summary>
    private void UpdatePositions()
    {
        foreach (var (_, entity) in _state.Entities)
        {
            if (!entity.IsAlive) continue;
            if (entity.VelocityX == 0 && entity.VelocityY == 0) continue;

            var newX = entity.X + entity.VelocityX;
            var newY = entity.Y + entity.VelocityY;

            // WHY SEPARATE AXIS CHECKS: Checking X and Y independently allows
            // "wall sliding" — if you walk diagonally into a wall, you still
            // slide along it in the non-blocked direction. This feels much better
            // than stopping completely on any wall contact.
            if (_state.Map != null)
            {
                if (!_state.Map.IsWalkableF(newX, entity.Y))
                {
                    newX = entity.X; // X blocked
                }
                if (!_state.Map.IsWalkableF(entity.X, newY))
                {
                    newY = entity.Y; // Y blocked
                }
                // Diagonal case: both axes moved but combined position is blocked
                if (newX != entity.X && newY != entity.Y && !_state.Map.IsWalkableF(newX, newY))
                {
                    if (_state.Map.IsWalkableF(newX, entity.Y))
                    {
                        newY = entity.Y; // Slide along X axis
                    }
                    else if (_state.Map.IsWalkableF(entity.X, newY))
                    {
                        newX = entity.X; // Slide along Y axis
                    }
                    else
                    {
                        newX = entity.X;
                        newY = entity.Y; // Completely blocked
                    }
                }
            }

            if (newX != entity.X || newY != entity.Y)
            {
                entity.X = newX;
                entity.Y = newY;
                entity.IsDirty = true;
            }

            // WHY ZERO VELOCITY FOR PLAYERS: Players stop immediately when input stops.
            // Without this, they'd keep drifting. Enemies keep their velocity (set by AI each tick).
            if (entity.Type == EntityType.Player)
            {
                entity.VelocityX = 0;
                entity.VelocityY = 0;
            }
        }
    }

    /// <summary>
    /// Move projectiles along their velocity vector and remove expired ones.
    /// Projectiles are removed when they exceed their range or hit a wall.
    /// </summary>
    private void UpdateProjectiles()
    {
        var toRemove = new List<string>();

        foreach (var projectile in _state.GetProjectiles())
        {
            projectile.X += projectile.VelocityX;
            projectile.Y += projectile.VelocityY;
            projectile.DistanceTraveled += MathF.Sqrt(
                projectile.VelocityX * projectile.VelocityX +
                projectile.VelocityY * projectile.VelocityY);
            projectile.IsDirty = true;

            // Remove if traveled beyond max range
            if (projectile.DistanceTraveled >= projectile.Range)
            {
                toRemove.Add(projectile.Id);
                continue;
            }

            // Remove if hit a wall
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

    /// <summary>
    /// Check projectile-entity collisions using simple circle overlap.
    /// 
    /// WHY CIRCLE COLLISION: With small entities (0.4 tile radius) and fast projectiles,
    /// simple distance checks are sufficient and very fast. AABB or SAT would add
    /// complexity without meaningful accuracy improvement at this scale.
    /// 
    /// WHY FACTION CHECKS: Player projectiles only hit enemies, enemy projectiles only
    /// hit players. This prevents friendly fire (which would be frustrating in co-op)
    /// and self-damage (projectiles spawn at the entity's position).
    /// </summary>
    private void CheckCollisions()
    {
        var projectilesToRemove = new List<string>();

        foreach (var projectile in _state.GetProjectiles())
        {
            foreach (var (_, target) in _state.Entities)
            {
                if (!target.IsAlive) continue;
                if (target.Id == projectile.SourceEntityId) continue;
                if (target.Type == EntityType.Projectile) continue;

                // Faction check: determine if this projectile should hit this target
                // Rules:
                //   - Co-op player projectiles → hit enemies AND invader
                //   - Invader projectiles → hit co-op players only
                //   - Enemy projectiles → hit co-op players AND invader
                var sourceEntity = _state.Entities.GetValueOrDefault(projectile.SourceEntityId ?? "");
                if (sourceEntity == null) continue;

                bool shouldHit = false;
                if (sourceEntity.Type == EntityType.Player && !sourceEntity.IsInvader)
                {
                    // Co-op player: hits enemies and invader players
                    shouldHit = target.Type == EntityType.Enemy ||
                                (target.Type == EntityType.Player && target.IsInvader);
                }
                else if (sourceEntity.Type == EntityType.Player && sourceEntity.IsInvader)
                {
                    // Invader: hits co-op players only
                    shouldHit = target.Type == EntityType.Player && !target.IsInvader;
                }
                else if (sourceEntity.Type == EntityType.Enemy)
                {
                    // Enemy: hits all players (both co-op and invader)
                    shouldHit = target.Type == EntityType.Player;
                }

                if (!shouldHit) continue;

                // Circle-circle overlap test
                var dx = projectile.X - target.X;
                var dy = projectile.Y - target.Y;
                var distSq = dx * dx + dy * dy;
                const float hitRadius = 0.4f;

                if (distSq <= hitRadius * hitRadius)
                {
                    var killed = target.TakeDamage(projectile.Damage);

                    // Track invader kills: award Cryptol when invader kills a co-op player
                    if (killed && target.Type == EntityType.Player && !target.IsInvader
                        && sourceEntity.Type == EntityType.Player && sourceEntity.IsInvader)
                    {
                        // Award 500 Cryptol for each co-op player killed
                        var invaderOwnerId = sourceEntity.OwnerId;
                        if (invaderOwnerId != null && Session != null)
                        {
                            // Check if this was the last alive co-op player
                            var aliveCoopPlayers = 0;
                            foreach (var (_, e) in _state.Entities)
                            {
                                if (e.Type == EntityType.Player && e.IsAlive && !e.IsInvader && e.Id != target.Id)
                                    aliveCoopPlayers++;
                            }

                            var award = 500;
                            var message = "+500 Cryptol (kill)";
                            if (aliveCoopPlayers == 0)
                            {
                                award = 1000; // 500 base + 500 bonus for last remaining
                                message = "+1000 Cryptol (final kill bonus!)";
                            }

                            // Broadcast award to the invader
                            _ = _connectionManager.SendToAsync(invaderOwnerId, new GameMessage
                            {
                                Type = MessageTypes.GameEvent,
                                GameEvent = new GameEventPayload
                                {
                                    Event = "cryptol_award",
                                    Amount = award,
                                    Message = message
                                }
                            });
                        }
                    }

                    // Lightning bolts pass through entities (hit multiple targets)
                    // All other projectiles are consumed on first hit
                    if (projectile.SubType != "lightning_bolt")
                    {
                        projectilesToRemove.Add(projectile.Id);
                    }
                    break; // Each projectile hits at most one target per tick
                }
            }
        }

        foreach (var id in projectilesToRemove)
        {
            _state.RemoveEntity(id);
        }
    }

    /// <summary>
    /// Decrement all cooldown timers by 1 tick.
    /// Cooldowns are set when abilities are used and prevent spam-firing.
    /// </summary>
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

    /// <summary>
    /// Broadcast the current game state to all connected clients.
    /// 
    /// WHY DELTA UPDATES: Only entities with IsDirty=true are sent. This typically
    /// reduces the payload from all ~50+ entities to just the 10-15 that moved or
    /// changed this tick. At 20Hz with 8 players, this saves significant bandwidth.
    /// 
    /// WHY PERSONALIZED MESSAGES: Each player needs their own lastProcessedInput value
    /// for client-side prediction reconciliation. We send a separate message per player
    /// rather than one broadcast (small cost for correct prediction).
    /// </summary>
    private void BroadcastState()
    {
        if (_connectionManager.ConnectionCount == 0) return;

        var dirtyEntities = _state.GetDirtyEntities().ToArray();
        if (dirtyEntities.Length == 0) return;

        // Build entity state array once (shared across all player messages)
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
                IsAlive = e.IsAlive,
                MedKits = e.MedKits
            };
        }

        // Send personalized state to each player
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

            // Fire-and-forget: don't await sends (would block the game loop thread).
            // If a send fails, the connection will be cleaned up on the next receive failure.
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
