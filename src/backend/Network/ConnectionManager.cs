using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Carcosa.Server.Network;

/// <summary>
/// Manages all active WebSocket connections and provides broadcast capabilities.
/// Thread-safe for concurrent access from the game loop and HTTP request threads.
/// </summary>
public sealed class ConnectionManager
{
    private readonly ConcurrentDictionary<string, ClientConnection> _connections = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public ConnectionManager()
    {
        _jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = GameJsonContext.Default
        };
    }

    public int ConnectionCount => _connections.Count;
    public int MaxConnections { get; set; } = 8;

    public event Action<string, string>? OnPlayerConnected;   // playerId, playerName
    public event Action<string>? OnPlayerDisconnected;         // playerId
    public event Action<string, GameMessage>? OnMessageReceived; // playerId, message

    /// <summary>
    /// Attempt to add a new connection. Returns false if server is full.
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
    /// Remove a connection and clean up resources.
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
            // Client disconnected abruptly
        }
        catch (OperationCanceledException)
        {
            // Server shutting down
        }
        finally
        {
            RemoveConnection(playerId);
        }
    }

    /// <summary>
    /// Send a message to a specific client.
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
            RemoveConnection(playerId);
        }
    }

    /// <summary>
    /// Broadcast a message to all connected clients.
    /// </summary>
    public async Task BroadcastAsync(GameMessage message, CancellationToken cancellationToken = default)
    {
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
    /// Broadcast a message to all clients except one (typically the sender).
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
    /// Get all connected player IDs.
    /// </summary>
    public IEnumerable<string> GetConnectedPlayerIds() => _connections.Keys;

    /// <summary>
    /// Get player info for a specific connection.
    /// </summary>
    public ClientConnection? GetConnection(string playerId)
    {
        _connections.TryGetValue(playerId, out var connection);
        return connection;
    }

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
/// Represents a single connected client.
/// </summary>
public sealed class ClientConnection
{
    public string PlayerId { get; }
    public string PlayerName { get; }
    public WebSocket WebSocket { get; }
    public SemaphoreSlim SendSemaphore { get; } = new(1, 1);
    public DateTime ConnectedAt { get; } = DateTime.UtcNow;

    public ClientConnection(string playerId, string playerName, WebSocket webSocket)
    {
        PlayerId = playerId;
        PlayerName = playerName;
        WebSocket = webSocket;
    }
}
