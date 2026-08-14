// =============================================================================
// SessionManager.cs — Game Session Lifecycle Management
// =============================================================================
//
// WHY SESSION MANAGER:
// The session lifecycle (lobby → playing → game over → lobby) is separate from
// the game loop because:
//   - Lobby state (class selection, ready status) doesn't need tick-based updates
//   - Session transitions are event-driven (player actions), not time-driven
//   - The session manager handles concerns the game loop doesn't (host assignment,
//     player counts, starting conditions)
//
// PEER-HOSTED MODEL:
// The first player to connect becomes the host. Only the host can start the game.
// If the host disconnects, the next player becomes the new host. This avoids
// needing a separate matchmaking step for casual play — just share your IP.
//
// THREAD SAFETY:
// Session methods are called from WebSocket handler threads (via Program.cs message
// routing). The _lock object serializes access to session state to prevent races
// (e.g., two players readying up simultaneously, or disconnect during game start).
// =============================================================================

using Carcosa.Server.Network;
using Carcosa.Server.Cryptol;

namespace Carcosa.Server.Game;

/// <summary>
/// Manages the game session lifecycle: lobby, class selection, game start, game over.
/// Peer-hosted model: the first player to connect becomes the host.
/// 
/// WHY LOCK INSTEAD OF ConcurrentDictionary: Session operations often need to
/// read multiple values and make decisions atomically (e.g., "are all players ready?
/// if so, start"). ConcurrentDictionary only provides per-key atomicity. A lock
/// gives us transaction-like semantics for multi-step operations.
/// </summary>
public sealed class SessionManager
{
    private readonly ConnectionManager _connectionManager;
    private readonly GameLoop _gameLoop;
    private readonly CryptolStore? _cryptolStore;
    private readonly Dictionary<string, PlayerSession> _players = new();
    private readonly object _lock = new();

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string? HostId { get; private set; }
    public SessionState State { get; private set; } = SessionState.Lobby;
    public int MaxPlayers { get; set; } = 8;
    /// <summary>Selected scenario for this session. Set by host in lobby.</summary>
    public MapScenario SelectedScenario { get; set; } = MapScenario.DrownedDock;
    /// <summary>Player ID of the invader (if one has joined). Null if no invader.</summary>
    public string? InvaderId { get; private set; }

    public SessionManager(ConnectionManager connectionManager, GameLoop gameLoop, CryptolStore? cryptolStore = null)
    {
        _connectionManager = connectionManager;
        _gameLoop = gameLoop;
        _cryptolStore = cryptolStore;
    }

    /// <summary>
    /// Handle a new player joining the session.
    /// </summary>
    public PlayerSession AddPlayer(string playerId, string playerName)
    {
        lock (_lock)
        {
            var session = new PlayerSession
            {
                PlayerId = playerId,
                PlayerName = playerName,
                SelectedClass = null,
                IsReady = false,
                IsHost = _players.Count == 0 // First player is host
            };

            _players[playerId] = session;

            if (session.IsHost)
            {
                HostId = playerId;
                Console.WriteLine($"[Session] {playerName} is the host");
            }

            // Broadcast updated session info to all players
            BroadcastSessionInfo();

            return session;
        }
    }

    /// <summary>
    /// Handle a player leaving the session.
    /// </summary>
    public void RemovePlayer(string playerId)
    {
        lock (_lock)
        {
            _players.Remove(playerId);

            // If the host left, assign a new host
            if (playerId == HostId)
            {
                HostId = _players.Keys.FirstOrDefault();
                if (HostId != null && _players.TryGetValue(HostId, out var newHost))
                {
                    newHost.IsHost = true;
                    Console.WriteLine($"[Session] New host: {newHost.PlayerName}");
                }
            }

            // Remove player entity from game
            _gameLoop.RemovePlayer(playerId);

            // Broadcast updated session info
            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Handle a player selecting their class.
    /// </summary>
    public void SelectClass(string playerId, string className)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var player)) return;

            // Validate class name
            if (className is not ("gangster" or "detective" or "surgeon")) return;

            player.SelectedClass = className;
            Console.WriteLine($"[Session] {player.PlayerName} selected {className}");

            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Handle a player toggling their ready state.
    /// </summary>
    public void SetReady(string playerId, bool ready)
    {
        lock (_lock)
        {
            if (!_players.TryGetValue(playerId, out var player)) return;

            // Must have selected a class to be ready
            if (ready && player.SelectedClass == null) return;

            player.IsReady = ready;
            Console.WriteLine($"[Session] {player.PlayerName} is {(ready ? "ready" : "not ready")}");

            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Host starts the game. Requires at least 1 player ready (for testing; normally 2+).
    /// </summary>
    public bool TryStartGame(string requesterId)
    {
        lock (_lock)
        {
            // Only host can start
            if (requesterId != HostId) return false;

            // Must be in lobby
            if (State != SessionState.Lobby) return false;

            // All players must be ready
            var readyPlayers = _players.Values.Where(p => p.IsReady).ToList();
            if (readyPlayers.Count == 0) return false;

            // Check all have selected classes
            if (readyPlayers.Any(p => p.SelectedClass == null)) return false;

            // Start the game!
            State = SessionState.Playing;
            Console.WriteLine($"[Session] Game starting with {readyPlayers.Count} players!");

            // Generate map based on selected scenario
            var seed = Random.Shared.Next();
            _gameLoop.State.Scenario = SelectedScenario;
            _gameLoop.State.Map = SelectedScenario switch
            {
                MapScenario.PallidSanctum => MapGenerator.GenerateTemple(100, 100, seed),
                MapScenario.MountainCave => MapGenerator.GenerateCave(60, 50, seed),
                _ => MapGenerator.Generate(80, 60, seed) // Warehouse (default)
            };
            _gameLoop.State.Phase = GamePhase.Playing;
            Console.WriteLine($"[Map] Generated {SelectedScenario} map with seed {seed}");

            // Send map to all players
            var mapMessage = new GameMessage
            {
                Type = MessageTypes.MapData,
                MapData = new MapDataPayload
                {
                    Width = _gameLoop.State.Map.Width,
                    Height = _gameLoop.State.Map.Height,
                    Seed = _gameLoop.State.Map.Seed,
                    TilesBase64 = _gameLoop.State.Map.ToBase64()
                }
            };
            _ = _connectionManager.BroadcastAsync(mapMessage);

            // Spawn player entities
            foreach (var player in _players.Values)
            {
                if (!player.IsReady) continue;

                var (spawnX, spawnY) = _gameLoop.State.Map.FindPlayerSpawn(Random.Shared);
                _gameLoop.AddPlayer(
                    player.PlayerId,
                    player.PlayerName,
                    player.SelectedClass ?? "detective",
                    spawnX,
                    spawnY);
            }

            // Start wave system
            _gameLoop.Waves.StartWaves(_gameLoop.State);

            BroadcastSessionInfo();
            return true;
        }
    }

    /// <summary>
    /// Invite a bot player to the current lobby. The bot auto-selects a class and readies up.
    /// </summary>
    private void InviteBot()
    {
        lock (_lock)
        {
            var botId = $"bot_{Guid.NewGuid().ToString("N")[..6]}";
            var botName = $"Bot_{_players.Count}";
            var classes = new[] { "gangster", "detective", "surgeon" };
            var selectedClass = classes[Random.Shared.Next(classes.Length)];

            var session = new PlayerSession
            {
                PlayerId = botId,
                PlayerName = botName,
                SelectedClass = selectedClass,
                IsReady = true,
                IsHost = false
            };
            _players[botId] = session;

            Console.WriteLine($"[Session] Bot invited: {botName} ({selectedClass})");
            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Allow a player to join the active game as an invader (PvP hostile).
    /// Only one invader per session. Must be mid-game (playing state).
    /// </summary>
    public void TryJoinAsInvader(string playerId)
    {
        lock (_lock)
        {
            // Must be in a playing game
            if (State != SessionState.Playing) return;

            // Only one invader allowed
            if (InvaderId != null) return;

            // Player must be in the session
            if (!_players.TryGetValue(playerId, out var player)) return;

            InvaderId = playerId;
            Console.WriteLine($"[Session] {player.PlayerName} joined as INVADER!");

            // Spawn the invader entity in the game
            if (_gameLoop.State.Map != null)
            {
                var (spawnX, spawnY) = _gameLoop.State.Map.FindPlayerSpawn(Random.Shared);
                var entity = _gameLoop.AddPlayer(playerId, player.PlayerName, "invader", spawnX, spawnY);
                entity.IsInvader = true;
                entity.Health = 150; // Invaders are slightly tankier
                entity.MaxHealth = 150;
                entity.MedKits = 0;
            }

            // Notify all players
            _ = _connectionManager.BroadcastAsync(new GameMessage
            {
                Type = MessageTypes.GameEvent,
                GameEvent = new GameEventPayload
                {
                    Event = "invader_joined",
                    Message = $"An INVADER has joined the fight!"
                }
            });

            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// End the game (victory or defeat).
    /// </summary>
    public void EndGame(bool victory)
    {
        lock (_lock)
        {
            State = victory ? SessionState.Victory : SessionState.GameOver;
            _gameLoop.State.Phase = victory ? GamePhase.Victory : GamePhase.GameOver;
            Console.WriteLine($"[Session] Game ended: {(victory ? "VICTORY" : "DEFEAT")}");

            // Award Cryptol to all connected players
            // Warehouse Victory (boss defeated, at least 1 survivor): 1000 Cryptol each
            // Warehouse Defeat (all players died): 10 Cryptol each (consolation)
            // Temple (always defeat — endless mode): 10 Cryptol per wave survived
            int amount;
            if (_gameLoop.State.Scenario == MapScenario.PallidSanctum)
            {
                amount = _gameLoop.State.CurrentWave * 10; // 10 per wave survived
            }
            else
            {
                amount = victory ? 1000 : 10;
            }
            var playerIds = _players.Keys.ToList();

            if (_cryptolStore != null && playerIds.Count > 0)
            {
                _cryptolStore.AwardCryptolBatch(playerIds, amount);
                Console.WriteLine($"[Cryptol] Awarded {amount} Cryptol to {playerIds.Count} players");

                // Broadcast the Cryptol award event to all players
                _ = _connectionManager.BroadcastAsync(new GameMessage
                {
                    Type = MessageTypes.GameEvent,
                    GameEvent = new GameEventPayload
                    {
                        Event = "cryptol_award",
                        Amount = amount,
                        Message = victory
                            ? $"Victory! +{amount} Cryptol"
                            : $"+{amount} Cryptol (stayed connected)"
                    }
                });
            }

            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Reset to lobby state (for replay).
    /// </summary>
    public void ResetToLobby()
    {
        lock (_lock)
        {
            State = SessionState.Lobby;
            _gameLoop.State.Phase = GamePhase.Lobby;
            _gameLoop.State.Map = null;
            _gameLoop.State.CurrentWave = 0;

            // Clear all entities
            foreach (var key in _gameLoop.State.Entities.Keys.ToList())
            {
                _gameLoop.State.Entities.TryRemove(key, out _);
            }

            // Reset ready states
            foreach (var player in _players.Values)
            {
                player.IsReady = false;
            }

            BroadcastSessionInfo();
        }
    }

    /// <summary>
    /// Process a session-related message from a client.
    /// </summary>
    public void HandleMessage(string playerId, GameMessage message)
    {
        if (message.Type == MessageTypes.SessionAction && message.SessionAction != null)
        {
            switch (message.SessionAction.Action)
            {
                case "select_class":
                    SelectClass(playerId, message.SessionAction.Value ?? "");
                    break;
                case "set_ready":
                    SetReady(playerId, message.SessionAction.Value == "true");
                    break;
                case "start_game":
                    TryStartGame(playerId);
                    break;
                case "return_to_lobby":
                    if (playerId == HostId)
                        ResetToLobby();
                    break;
                case "select_scenario":
                    if (playerId == HostId && State == SessionState.Lobby)
                    {
                        SelectedScenario = message.SessionAction.Value switch
                        {
                            "temple" => MapScenario.PallidSanctum,
                            "mountain_cave" or "cave" => MapScenario.MountainCave,
                            "hollow" => MapScenario.Hollow,
                            _ => MapScenario.DrownedDock
                        };
                        Console.WriteLine($"[Session] Scenario set to {SelectedScenario}");
                        BroadcastSessionInfo();
                    }
                    break;
                case "join_as_invader":
                    TryJoinAsInvader(playerId);
                    break;
                case "invite_bot":
                    if (playerId == HostId && State == SessionState.Lobby)
                    {
                        InviteBot();
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Get current session info for a newly connecting player.
    /// </summary>
    public SessionInfoPayload GetSessionInfo()
    {
        lock (_lock)
        {
            return new SessionInfoPayload
            {
                SessionId = SessionId,
                HostId = HostId ?? "",
                State = State switch
                {
                    SessionState.Lobby => "lobby",
                    SessionState.Playing => "playing",
                    SessionState.GameOver => "game_over",
                    SessionState.Victory => "victory",
                    _ => "lobby"
                },
                Players = _players.Values.Select(p => new PlayerInfo
                {
                    Id = p.PlayerId,
                    Name = p.PlayerName,
                    SelectedClass = p.SelectedClass,
                    IsReady = p.IsReady,
                    IsHost = p.IsHost
                }).ToArray(),
                MaxPlayers = MaxPlayers,
                CurrentWave = _gameLoop.State.CurrentWave,
                Scenario = SelectedScenario switch
                {
                    MapScenario.PallidSanctum => "pallid_sanctum",
                    MapScenario.Hollow => "hollow",
                    MapScenario.MountainCave => "mountain_cave",
                    _ => "drowned_dock"
                }
            };
        }
    }

    private void BroadcastSessionInfo()
    {
        var info = GetSessionInfo();
        var message = new GameMessage
        {
            Type = MessageTypes.SessionInfo,
            SessionInfo = info
        };
        _ = _connectionManager.BroadcastAsync(message);
    }
}

/// <summary>
/// Tracks per-player session state (class, ready status, etc.).
/// </summary>
public sealed class PlayerSession
{
    public required string PlayerId { get; init; }
    public required string PlayerName { get; init; }
    public string? SelectedClass { get; set; }
    public bool IsReady { get; set; }
    public bool IsHost { get; set; }
}

public enum SessionState
{
    Lobby,
    Playing,
    GameOver,
    Victory
}
