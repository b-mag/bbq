// =============================================================================
// GameFlowSystem.cs — Player Death, Revive, and Game-Over Logic
// =============================================================================
//
// WHY SEPARATE FROM GAMELOOP:
// Death/revive mechanics are complex enough to warrant their own system:
//   - Players become "downed" (not permanently dead) — they can be revived
//   - Reviving requires holding interact near a downed ally for 3 seconds
//   - Game over only triggers when ALL players are simultaneously downed
//   - Various events need to be broadcast (death, revive, game over)
//
// DESIGN DECISIONS:
//   - Players are downed, not removed. Their entity persists with IsAlive=false.
//     This allows revive mechanics and lets the client render death markers.
//   - Game over checks run every tick (cheap: just iterate players).
//   - Dead enemies are removed immediately to free memory and reduce entity count.
//   - Broadcast calls use fire-and-forget (don't block game loop for network I/O).
// =============================================================================

using Carcosa.Server.Gameplay;
using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// Manages game flow: player death/incapacitation, revive mechanics,
/// game over detection, and victory conditions.
/// </summary>
public sealed class GameFlowSystem
{
    private const int ReviveTicks = 60; // 3 seconds to revive (hold E near downed ally)
    private const float ReviveRange = 2f;

    private readonly ConnectionManager _connectionManager;
    private readonly Dictionary<string, int> _reviveProgress = new(); // entityId → ticks held

    public GameFlowSystem(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// Update game flow checks each tick.
    /// </summary>
    public void Update(GameState state, SessionManager sessionManager)
    {
        if (state.Phase != GamePhase.Playing && state.Phase != GamePhase.WaveIntermission) return;

        RespawnDeadPlayers(state);
        CheckGameOver(state, sessionManager);
        CleanupDeadEnemies(state, sessionManager);
    }

    /// <summary>
    /// Process revive interaction (player pressing E near downed ally).
    /// Called from input processing when interact is true.
    /// </summary>
    public void ProcessReviveInteraction(GameState state, Entity interactor)
    {
        if (!interactor.IsAlive) return;

        // Find nearest downed player within range
        Entity? downedAlly = null;
        float nearestDist = float.MaxValue;

        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Player) continue;
            if (entity.IsAlive) continue; // Must be downed
            if (entity.Id == interactor.Id) continue;

            var dx = entity.X - interactor.X;
            var dy = entity.Y - interactor.Y;
            var dist = MathF.Sqrt(dx * dx + dy * dy);

            if (dist <= ReviveRange && dist < nearestDist)
            {
                nearestDist = dist;
                downedAlly = entity;
            }
        }

        if (downedAlly == null)
        {
            // No one to revive, clear progress
            _reviveProgress.Remove(interactor.Id);
            return;
        }

        // Increment revive progress
        var key = interactor.Id;
        _reviveProgress.TryGetValue(key, out var progress);
        progress++;
        _reviveProgress[key] = progress;

        if (progress >= ReviveTicks)
        {
            // Revive complete!
            downedAlly.IsAlive = true;
            downedAlly.Health = downedAlly.MaxHealth / 2; // Revive at 50% HP
            downedAlly.IsDirty = true;
            _reviveProgress.Remove(key);

            // Broadcast revive event
            _ = _connectionManager.BroadcastAsync(new GameMessage
            {
                Type = MessageTypes.GameEvent,
                GameEvent = new GameEventPayload
                {
                    Event = "revive",
                    TargetId = downedAlly.Id,
                    SourceId = interactor.Id,
                    X = downedAlly.X,
                    Y = downedAlly.Y,
                    Message = "Revived!"
                }
            });

            Console.WriteLine($"[Game] {interactor.Id} revived {downedAlly.Id}");
        }
    }

    /// <summary>
    /// Lethal damage returns the player to the dungeon entrance at full HP.
    /// Same character as overworld — no downed/spectate loop.
    /// </summary>
    public void OnPlayerDowned(GameState state, Entity player)
    {
        player.IsAlive = true;
        player.Health = player.MaxHealth;
        player.VelocityX = 0;
        player.VelocityY = 0;
        if (state.Map != null)
        {
            var (x, y) = DungeonRules.GetEntrancePosition(state.Map);
            player.X = x;
            player.Y = y;
        }
        player.IsDirty = true;

        _ = _connectionManager.BroadcastAsync(new GameMessage
        {
            Type = MessageTypes.GameEvent,
            GameEvent = new GameEventPayload
            {
                Event = "respawn",
                TargetId = player.Id,
                X = player.X,
                Y = player.Y,
                Message = "You fall. The dungeon returns you to the entrance."
            }
        });

        Console.WriteLine($"[Game] Player {player.Id} returned to dungeon entrance.");
    }

    private void RespawnDeadPlayers(GameState state)
    {
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Player) continue;
            if (entity.IsAlive) continue;
            OnPlayerDowned(state, entity);
        }
    }

    /// <summary>
    /// Broadcast a damage event for client-side feedback (floating numbers, etc.).
    /// </summary>
    public void BroadcastDamageEvent(Entity target, int amount, Entity? source)
    {
        _ = _connectionManager.BroadcastAsync(new GameMessage
        {
            Type = MessageTypes.GameEvent,
            GameEvent = new GameEventPayload
            {
                Event = "damage",
                TargetId = target.Id,
                SourceId = source?.Id,
                Amount = amount,
                X = target.X,
                Y = target.Y
            }
        });
    }

    /// <summary>
    /// Broadcast a wave start event.
    /// </summary>
    public void BroadcastWaveStart(int wave)
    {
        _ = _connectionManager.BroadcastAsync(new GameMessage
        {
            Type = MessageTypes.GameEvent,
            GameEvent = new GameEventPayload
            {
                Event = "wave_start",
                Wave = wave,
                Message = wave == 5 ? "Final Wave - The Boss Approaches!" : $"Wave {wave}"
            }
        });
    }

    private void CheckGameOver(GameState state, SessionManager sessionManager)
    {
        // Check if any players are alive
        bool anyAlive = false;
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type == EntityType.Player && entity.IsAlive)
            {
                anyAlive = true;
                break;
            }
        }

        // Count total players (alive or downed)
        int totalPlayers = 0;
        foreach (var (_, entity) in state.Entities)
        {
            if (entity.Type == EntityType.Player)
                totalPlayers++;
        }

        // Game over if we have players but none are alive
        if (totalPlayers > 0 && !anyAlive)
        {
            sessionManager.EndGame(victory: false);

            _ = _connectionManager.BroadcastAsync(new GameMessage
            {
                Type = MessageTypes.GameEvent,
                GameEvent = new GameEventPayload
                {
                    Event = "game_over",
                    Message = "All investigators have fallen... Carcosa claims another victory."
                }
            });
        }
    }

    private void CleanupDeadEnemies(GameState state, SessionManager sessionManager)
    {
        var toRemove = new List<string>();
        foreach (var (id, entity) in state.Entities)
        {
            if (entity.Type != EntityType.Enemy || entity.IsAlive) continue;

            Entity? killer = null;
            if (!string.IsNullOrEmpty(entity.TaggedBy))
                killer = state.GetPlayerByOwnerId(entity.TaggedBy);
            if (killer == null && !string.IsNullOrEmpty(entity.AggroTargetId))
                state.Entities.TryGetValue(entity.AggroTargetId, out killer);
            if (killer != null && killer.Type == EntityType.Player)
                sessionManager.OnDungeonEnemyKilled(entity, killer);

            toRemove.Add(id);
        }

        foreach (var id in toRemove)
            state.RemoveEntity(id);
    }
}
