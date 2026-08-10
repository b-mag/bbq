using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// Manages the game session lifecycle: lobby, class selection, game start, game over.
/// Peer-hosted model: the first player to connect becomes the host.
/// </summary>
public sealed class SessionManager
{
    private readonly ConnectionManager _connectionManager;
    private readonly GameLoop _gameLoop;
    private readonly Dictionary<string, PlayerSession> _players = new();
    private readonly object _lock = new();

    public string SessionId { get; } = Guid.NewGuid().ToString("N")[..8];
    public string? HostId { get; private set; }
    public SessionState State { get; private set; } = SessionState.Lobby;
    public int MaxPlayers { get; set; } = 8;

    public SessionManager(ConnectionManager connectionManager, GameLoop gameLoop)
    {
        _connectionManager = connectionManager;
        _gameLoop = gameLoop;
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

            // Generate map
            var seed = Random.Shared.Next();
            _gameLoop.State.Map = MapGenerator.Generate(80, 60, seed);
            _gameLoop.State.Phase = GamePhase.Playing;
            Console.WriteLine($"[Map] Generated map with seed {seed}");

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
    /// End the game (victory or defeat).
    /// </summary>
    public void EndGame(bool victory)
    {
        lock (_lock)
        {
            State = victory ? SessionState.Victory : SessionState.GameOver;
            _gameLoop.State.Phase = victory ? GamePhase.Victory : GamePhase.GameOver;
            Console.WriteLine($"[Session] Game ended: {(victory ? "VICTORY" : "DEFEAT")}");
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
                CurrentWave = _gameLoop.State.CurrentWave
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
