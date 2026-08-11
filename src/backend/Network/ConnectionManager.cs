// =============================================================================
// ConnectionManager.cs — WebSocket Connection Hub
// =============================================================================
//
// WHY RAW WEBSOCKETS OVER SignalR:
// SignalR uses runtime proxy generation and dynamic dispatch, making it
// incompatible with Native AOT. Raw WebSockets give us full control over
// message framing, lower memory overhead, and deterministic behavior.
// For a real-time game server, this control is actually preferable — we need
// tight control over message ordering, broadcast patterns, and per-connection
// backpressure.
//
// THREAD SAFETY:
// This class is accessed from multiple threads simultaneously:
//   - HTTP thread pool threads (new connections arriving)
//   - The game loop thread (broadcasting state every tick)
//   - Individual WebSocket receive loops (one per player)
// ConcurrentDictionary handles the connection registry. Per-connection
// SemaphoreSlim(1,1) prevents concurrent writes to the same socket (WebSocket
// is not thread-safe for simultaneous sends).
//
// ARCHITECTURE:
// The ConnectionManager is intentionally "dumb" — it only knows how to add/remove
// connections and send/receive messages. All game logic (what to do with messages)
// lives in the game systems. Events (OnPlayerConnected, OnMessageReceived, etc.)
// decouple this from game logic.
// =============================================================================

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Carcosa.Server.Network;

/// <summary>
/// Manages all active WebSocket connections and provides broadcast capabilities.
/// Thread-safe for concurrent access from the game loop and HTTP request threads.
/// 
/// Design: The manager acts as a message bus — it doesn't interpret messages,
/// it only routes them. Game logic subscribes to events and decides what to do.
/// </summary>
public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public ConnectionManager()
    {
        // WHY: Use the source-generated context for JSON serialization.
        // This avoids reflection-based discovery of properties at runtime,
        // which is required for AOT but also faster in JIT mode.
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = GameJsonContext.Default
        };
    }

    public int ConnectionCount => _connections.Count;
    public int MaxConnections { get; set; } = 8;

    /// <summary>Fired when a player successfully connects. Used by SessionManager to track lobby state.</summary>
    public event Action<string, string>? OnPlayerConnected;   // playerId, playerName
    /// <summary>Fired when a player disconnects (clean or abrupt). Used for cleanup.</summary>
    public event Action<string>? OnPlayerDisconnected;         // playerId
    /// <summary>Fired for every valid JSON message received. The central dispatch point.</summary>
    public event Action<string, GameMessage>? OnMessageReceived; // playerId, message

    /// <summary>
    /// Attempt to add a new connection. Returns false if server is full.
    /// WHY TryAdd: ConcurrentDictionary.TryAdd is atomic — no lock needed.
    /// The connection count check + add is technically a race condition, but
    /// overshooting by 1 player is acceptable (the worst case is 9 instead of 8).
    /// </summary>
    public bool TryAddConnection(string playerId, string playerName, WebSocket webSocket)
    {
        if (_connections.Count >= MaxConnections)
            return false;

        var connection = new ClientConnection(playerId, playerName, webSocket);
        if (_connections.TryAdd(playerId, connection))
        {
            OnPlayerConnected?.Invoke(playerId, playerName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove a connection and fire the disconnected event.
    /// Called when the receive loop ends (either clean close or error).
    /// </summary>
    public void RemoveConnection(string playerId)
    {
        if (_connections.TryRemove(playerId, out _))
        {
            OnPlayerDisconnected?.Invoke(playerId);
        }
    }

    /// <summary>
    /// Start receiving messages from a client. Blocks until the connection closes.
    /// 
    /// WHY BLOCKING: Each WebSocket connection runs its own receive loop on a thread pool
    /// thread. This is the standard ASP.NET pattern — the HTTP request "stays alive" for
    /// the duration of the WebSocket session. When the socket closes (or errors), we fall
    /// through to cleanup. The thread is returned to the pool between awaits.
    /// </summary>
    public async Task HandleConnectionAsync(string playerId, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(playerId, out var connection))
            return;

        var buffer = new byte[4096];

        try
        {
            while (connection.WebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await connection.WebSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await connection.WebSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    // WHY: Deserialize using the source-generated context.
                    // GameJsonContext.Default.GameMessage provides the pre-compiled
                    // deserializer — no reflection, no runtime code generation.
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var message = JsonSerializer.Deserialize(json, GameJsonContext.Default.GameMessage);

                    if (message != null)
                    {
                        OnMessageReceived?.Invoke(playerId, message);
                    }
                }
            }
        }
        catch (WebSocketException)
        {
            // Client disconnected abruptly — this is normal (browser tab closed, network drop)
        }
        catch (OperationCanceledException)
        {
            // Server shutting down — graceful exit
        }
        finally
        {
            RemoveConnection(playerId);
        }
    }

    /// <summary>
    /// Send a message to a specific client.
    /// 
    /// WHY SEMAPHORE: WebSocket.SendAsync is not thread-safe for concurrent calls.
    /// The game loop broadcasts state to all players every tick (50ms), and chat/event
    /// messages can arrive at any time. The semaphore serializes writes per-connection
    /// without blocking other connections.
    /// </summary>
    public async Task SendToAsync(string playerId, GameMessage message, CancellationToken cancellationToken = default)
    {
        if (!_connections.TryGetValue(playerId, out var connection))
            return;

        if (connection.WebSocket.State != WebSocketState.Open)
            return;

        var json = JsonSerializer.Serialize(message, GameJsonContext.Default.GameMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        try
        {
            await connection.SendSemaphore.WaitAsync(cancellationToken);
            try
            {
                await connection.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            finally
            {
                connection.SendSemaphore.Release();
            }
        }
        catch (WebSocketException)
        {
            // Connection died during send — will be cleaned up on next receive failure
            RemoveConnection(playerId);
        }
    }

    /// <summary>
    /// Broadcast a message to all connected clients in parallel.
    /// 
    /// WHY PARALLEL: With 8 players, sequential sends would add latency (each send
    /// awaits the kernel write). Parallel sends via Task.WhenAll let the OS handle
    /// buffering across all sockets simultaneously.
    /// </summary>
    public async Task BroadcastAsync(GameMessage message, CancellationToken cancellationToken = default)
    {
        // WHY: Serialize once, send the same bytes to everyone.
        // This avoids re-serializing the same object N times.
        var json = JsonSerializer.Serialize(message, GameJsonContext.Default.GameMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();
        foreach (var (playerId, connection) in _connections)
        {
            if (connection.WebSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendBytesAsync(connection, bytes, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Broadcast a message to all clients except one (typically the sender for chat relay).
    /// </summary>
    public async Task BroadcastExceptAsync(string excludePlayerId, GameMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, GameJsonContext.Default.GameMessage);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = new List<Task>();
        foreach (var (playerId, connection) in _connections)
        {
            if (playerId != excludePlayerId && connection.WebSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendBytesAsync(connection, bytes, cancellationToken));
            }
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Get all connected player IDs. Used by the game loop to send personalized state.
    /// </summary>
    public IEnumerable<string> GetConnectedPlayerIds() => _connections.Keys;

    /// <summary>
    /// Get player info for a specific connection (used for debugging/monitoring).
    /// </summary>
    public ClientConnection? GetConnection(string playerId)
    {
        _connections.TryGetValue(playerId, out var connection);
        return connection;
    }

    /// <summary>
    /// Send raw pre-serialized bytes to a connection. Used by broadcast methods
    /// to avoid re-serializing the same message for each recipient.
    /// </summary>
    private static async Task SendBytesAsync(ClientConnection connection, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            await connection.SendSemaphore.WaitAsync(cancellationToken);
            try
            {
                await connection.WebSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    endOfMessage: true,
                    cancellationToken);
            }
            finally
            {
                connection.SendSemaphore.Release();
            }
        }
        catch (WebSocketException)
        {
            // Connection lost — will be cleaned up on next receive failure
        }
    }
}

/// <summary>
/// Represents a single connected client with their WebSocket and send lock.
/// 
/// WHY A CLASS (not struct): Needs reference semantics for the SemaphoreSlim
/// (mutable state shared between send calls). Also stored in ConcurrentDictionary
/// which boxes value types anyway.
/// </summary>
public sealed class ClientConnection
{
    public string PlayerId { get; }
    public string PlayerName { get; }
    public WebSocket WebSocket { get; }
    /// <summary>
    /// Prevents concurrent WebSocket.SendAsync calls which would corrupt the frame stream.
    /// Initialized to (1,1) = binary semaphore = mutex behavior.
    /// </summary>
    public SemaphoreSlim SendSemaphore { get; } = new(1, 1);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    public ClientConnection(string playerId, string playerName, WebSocket webSocket)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        WebSocket = webSocket;
    }
}
