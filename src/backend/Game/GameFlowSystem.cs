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

        // Check for game over: all players dead/incapacitated
        CheckGameOver(state, sessionManager);

        // Clean up dead enemy entities (remove after a delay for death animation)
        CleanupDeadEnemies(state);
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
    /// Handle player taking lethal damage (called from collision/damage code).
    /// Players become incapacitated rather than permanently dead.
    /// </summary>
    public void OnPlayerDowned(GameState state, Entity player)
    {
        player.IsAlive = false;
        player.VelocityX = 0;
        player.VelocityY = 0;
        player.IsDirty = true;

        _ = _connectionManager.BroadcastAsync(new GameMessage
        {
            Type = MessageTypes.GameEvent,
            GameEvent = new GameEventPayload
            {
                Event = "death",
                TargetId = player.Id,
                X = player.X,
                Y = player.Y,
                Message = "Downed!"
            }
        });

        Console.WriteLine($"[Game] Player {player.Id} downed!");
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
                Message = wave == 5 ? "Final Wave - The Herald Approaches!" : $"Wave {wave}"
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

    private void CleanupDeadEnemies(GameState state)
    {
        // Remove dead enemies after they've been flagged for 2 seconds (40 ticks)
        var toRemove = new List<string>();
        foreach (var (id, entity) in state.Entities)
        {
            if (entity.Type == EntityType.Enemy && !entity.IsAlive)
            {
                toRemove.Add(id);
            }
        }

        foreach (var id in toRemove)
        {
            state.RemoveEntity(id);
        }
    }
}
