// =============================================================================
// InputQueue.cs — Thread-Safe Player Input Buffer
// =============================================================================
//
// WHY A SEPARATE QUEUE:
// Player inputs arrive on HTTP thread pool threads (from WebSocket receive loops)
// but must be processed deterministically on the game loop's dedicated thread.
// This queue decouples input reception from input processing:
//   - WebSocket threads enqueue inputs as they arrive (non-blocking, O(1))
//   - Game loop drains all pending inputs once per tick (deterministic ordering)
//
// WHY ConcurrentQueue:
// Multiple WebSocket connections enqueue simultaneously from different threads.
// ConcurrentQueue is lock-free for the enqueue path (uses Interlocked operations
// internally), which means WebSocket handlers never block each other or the game loop.
//
// WHY DRAIN-ALL PATTERN:
// Processing all inputs at once per tick (rather than one at a time) ensures that
// all players who sent input between ticks get processed in the same tick. This
// prevents timing-based advantage where one player's input is always processed
// first because their packets arrive slightly earlier.
// =============================================================================

using System.Collections.Concurrent;
using Carcosa.Server.Network;

namespace Carcosa.Server.Game;

/// <summary>
/// Thread-safe queue for player inputs received from WebSocket connections.
/// The game loop drains this queue each tick to process all pending inputs.
/// 
/// Flow: WebSocket handler → Enqueue() → [queue] → DrainAll() → GameLoop.ProcessInputs()
/// </summary>
public sealed class InputQueue
{
    private readonly ConcurrentQueue<PlayerInputEntry> _queue = new();

    /// <summary>
    /// Enqueue a player input for processing on the next game tick.
    /// Called from WebSocket receive threads (multiple threads may call concurrently).
    /// This is lock-free and non-blocking.
    /// </summary>
    public void Enqueue(string playerId, PlayerInputPayload input)
    {
        _queue.Enqueue(new PlayerInputEntry(playerId, input));
    }

    /// <summary>
    /// Drain all pending inputs from the queue atomically.
    /// Called once per tick by the game loop thread.
    /// Returns all inputs that accumulated since the last drain.
    /// 
    /// WHY LIST (not array): The count isn't known ahead of time and allocating
    /// a list that grows is simpler than pre-sizing. At 8 players × 1 input/tick,
    /// this is at most ~8 items — negligible allocation.
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
    /// Get the number of pending inputs (for monitoring/debugging).
    /// </summary>
    public int Count => _queue.Count;
}

/// <summary>
/// Associates a player ID with their input payload.
/// 
/// WHY RECORD STRUCT: Value type avoids heap allocation per input entry.
/// The readonly modifier ensures it's truly immutable once created.
/// Record gives us structural equality and ToString() for free.
/// </summary>
public readonly record struct PlayerInputEntry(string PlayerId, PlayerInputPayload Input);
