using Carcosa.Server.Game;
using Carcosa.Server.Network;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for InputQueue — thread-safe enqueue/drain behavior.
/// </summary>
public class InputQueueTests
{
    [Fact]
    public void DrainAll_ReturnsAllEnqueued()
    {
        var queue = new InputQueue();
        queue.Enqueue("player1", new PlayerInputPayload { SequenceNumber = 1 });
        queue.Enqueue("player2", new PlayerInputPayload { SequenceNumber = 2 });

        var results = queue.DrainAll();
        Assert.Equal(2, results.Count);
        Assert.Equal("player1", results[0].PlayerId);
        Assert.Equal("player2", results[1].PlayerId);
    }

    [Fact]
    public void DrainAll_EmptiesQueue()
    {
        var queue = new InputQueue();
        queue.Enqueue("p1", new PlayerInputPayload { SequenceNumber = 1 });

        queue.DrainAll();
        var second = queue.DrainAll();
        Assert.Empty(second);
    }

    [Fact]
    public void Count_ReflectsQueueSize()
    {
        var queue = new InputQueue();
        Assert.Equal(0, queue.Count);
        queue.Enqueue("p1", new PlayerInputPayload { SequenceNumber = 1 });
        Assert.Equal(1, queue.Count);
    }
}
