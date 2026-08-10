using System.Collections.Concurrent;
using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// Thread-safe queue for player inputs received from WebSocket connections.
/// The game loop drains this queue each tick to process all pending inputs.
/// </summary>
public sealed class InputQueue
{
    private readonly ConcurrentQueue<PlayerInputEntry> _queue = new();

    /// <summary>
    /// Enqueue a player input for processing on the next game tick.
    /// </summary>
    public void Enqueue(string playerId, PlayerInputPayload input)
    {
        _queue.Enqueue(new PlayerInputEntry(playerId, input));
    }

    /// <summary>
    /// Drain all pending inputs from the queue.
    /// Returns all inputs that were queued since last drain.
    /// </summary>
    public List<PlayerInputEntry> DrainAll()
    {
        var inputs = new List<PlayerInputEntry>();
        while (_queue.TryDequeue(out var entry))
        {
            inputs.Add(entry);
        }
        return inputs;
    }

    /// <summary>
    /// Get the number of pending inputs.
    /// </summary>
    public int Count => _queue.Count;
}

/// <summary>
/// A single input entry associating a player ID with their input payload.
/// </summary>
public readonly record struct PlayerInputEntry(string PlayerId, PlayerInputPayload Input);
