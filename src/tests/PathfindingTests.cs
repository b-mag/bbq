using Carcosa.Server.Game;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for Pathfinding — A* path finding and line-of-sight.
/// </summary>
public class PathfindingTests
{
    [Fact]
    public void FindPath_ReturnsPathOnOpenMap()
    {
        var map = MapGenerator.GenerateTemple(50, 50, 1); // Open arena
        var path = Pathfinding.FindPath(map, 10, 10, 20, 20);
        Assert.True(path.Count > 0);
        Assert.Equal((10, 10), path[0]);
    }

    [Fact]
    public void FindPath_ReturnsEmptyForUnreachable()
    {
        // Create a tiny map where goal is a wall
        var map = MapGenerator.Generate(20, 20, 1);
        // (0,0) is always a wall
        var path = Pathfinding.FindPath(map, 5, 5, 0, 0);
        Assert.Empty(path);
    }

    [Fact]
    public void HasLineOfSight_TrueForOpenPath()
    {
        var map = MapGenerator.GenerateTemple(50, 50, 1);
        // In an open temple, nearby points should have LOS
        var los = Pathfinding.HasLineOfSight(map, 10f, 10f, 12f, 12f);
        Assert.True(los);
    }

    [Fact]
    public void GetDirectionToward_ReturnsNonZero()
    {
        var map = MapGenerator.GenerateTemple(50, 50, 1);
        var (dx, dy) = Pathfinding.GetDirectionToward(map, 10f, 10f, 20f, 20f);
        // Should be a normalized direction vector
        Assert.True(dx != 0 || dy != 0);
        var magnitude = MathF.Sqrt(dx * dx + dy * dy);
        Assert.True(magnitude > 0.9f && magnitude < 1.1f); // ~unit vector
    }
}
