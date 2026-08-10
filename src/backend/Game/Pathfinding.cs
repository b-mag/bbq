namespace Carcosa.Server.Game;

/// <summary>
/// Simple A* pathfinding on the tile grid.
/// Used by AI enemies to navigate toward players.
/// </summary>
public static class Pathfinding
{
    private const int MaxSearchNodes = 500; // Limit to prevent expensive searches

    /// <summary>
    /// Find a path from start to goal on the tile map.
    /// Returns a list of tile positions, or empty if no path found.
    /// </summary>
    public static List<(int X, int Y)> FindPath(TileMap map, int startX, int startY, int goalX, int goalY)
    {
        if (!map.IsWalkable(goalX, goalY)) return [];

        var openSet = new PriorityQueue<(int X, int Y), float>();
        var cameFrom = new Dictionary<(int, int), (int, int)>();
        var gScore = new Dictionary<(int, int), float>();
        var fScore = new Dictionary<(int, int), float>();

        var start = (startX, startY);
        var goal = (goalX, goalY);

        gScore[start] = 0;
        fScore[start] = Heuristic(startX, startY, goalX, goalY);
        openSet.Enqueue((startX, startY), fScore[start]);

        var explored = 0;

        while (openSet.Count > 0 && explored < MaxSearchNodes)
        {
            var current = openSet.Dequeue();
            explored++;

            if (current.X == goalX && current.Y == goalY)
            {
                return ReconstructPath(cameFrom, current);
            }

            // Check 4 cardinal directions
            ReadOnlySpan<(int dx, int dy)> neighbors = [(0, -1), (0, 1), (-1, 0), (1, 0)];
            foreach (var (dx, dy) in neighbors)
            {
                var nx = current.X + dx;
                var ny = current.Y + dy;

                if (!map.IsWalkable(nx, ny)) continue;

                var neighbor = (nx, ny);
                var tentativeG = gScore.GetValueOrDefault(current, float.MaxValue) + 1f;

                if (tentativeG < gScore.GetValueOrDefault(neighbor, float.MaxValue))
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    var f = tentativeG + Heuristic(nx, ny, goalX, goalY);
                    fScore[neighbor] = f;
                    openSet.Enqueue((nx, ny), f);
                }
            }
        }

        return []; // No path found
    }

    /// <summary>
    /// Get the next movement direction toward a target using A*.
    /// Returns a normalized direction vector, or (0,0) if no path.
    /// </summary>
    public static (float DirX, float DirY) GetDirectionToward(
        TileMap map, float fromX, float fromY, float toX, float toY)
    {
        var path = FindPath(map, (int)fromX, (int)fromY, (int)toX, (int)toY);

        if (path.Count < 2) return (0, 0);

        // Move toward the next tile in the path
        var nextTile = path[1]; // path[0] is current position
        var dx = nextTile.X + 0.5f - fromX;
        var dy = nextTile.Y + 0.5f - fromY;
        var magnitude = MathF.Sqrt(dx * dx + dy * dy);

        if (magnitude < 0.01f) return (0, 0);

        return (dx / magnitude, dy / magnitude);
    }

    /// <summary>
    /// Check line of sight between two points (Bresenham's line).
    /// Returns true if there are no walls between the points.
    /// </summary>
    public static bool HasLineOfSight(TileMap map, float x1, float y1, float x2, float y2)
    {
        int ix1 = (int)x1, iy1 = (int)y1;
        int ix2 = (int)x2, iy2 = (int)y2;

        int dx = Math.Abs(ix2 - ix1);
        int dy = Math.Abs(iy2 - iy1);
        int sx = ix1 < ix2 ? 1 : -1;
        int sy = iy1 < iy2 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            if (ix1 == ix2 && iy1 == iy2) return true;

            if (!map.IsWalkable(ix1, iy1)) return false;

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; ix1 += sx; }
            if (e2 < dx) { err += dx; iy1 += sy; }
        }
    }

    private static float Heuristic(int x1, int y1, int x2, int y2)
    {
        // Manhattan distance
        return Math.Abs(x1 - x2) + Math.Abs(y1 - y2);
    }

    private static List<(int X, int Y)> ReconstructPath(
        Dictionary<(int, int), (int, int)> cameFrom, (int X, int Y) current)
    {
        var path = new List<(int X, int Y)> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}
