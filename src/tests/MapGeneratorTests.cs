using Carcosa.Server.Game;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for MapGenerator — map generation produces valid, playable maps.
/// </summary>
public class MapGeneratorTests
{
    [Fact]
    public void Generate_ProducesCorrectDimensions()
    {
        var map = MapGenerator.Generate(80, 60, 12345);
        Assert.Equal(80, map.Width);
        Assert.Equal(60, map.Height);
        Assert.Equal(80 * 60, map.Tiles.Length);
    }

    [Fact]
    public void Generate_HasSpawnPoints()
    {
        var map = MapGenerator.Generate(80, 60, 99999);
        Assert.True(map.SpawnPoints.Length > 0);
    }

    [Fact]
    public void Generate_HasRooms()
    {
        var map = MapGenerator.Generate(80, 60, 42);
        Assert.True(map.Rooms.Length > 0);
    }

    [Fact]
    public void Generate_SeededReproducibility()
    {
        var map1 = MapGenerator.Generate(80, 60, 777);
        var map2 = MapGenerator.Generate(80, 60, 777);
        Assert.Equal(map1.Tiles, map2.Tiles);
    }

    [Fact]
    public void GenerateTemple_ProducesOpenArena()
    {
        var map = MapGenerator.GenerateTemple(100, 100, 555);
        Assert.Equal(100, map.Width);
        Assert.Equal(100, map.Height);

        // Temple should be mostly open floor — count walkable tiles
        int walkable = 0;
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                if (map.IsWalkable(x, y)) walkable++;

        // At least 70% should be walkable (it's an open arena)
        Assert.True(walkable > map.Width * map.Height * 0.7);
    }

    [Fact]
    public void TileMap_IsWalkable_WallsBlock()
    {
        var map = MapGenerator.Generate(80, 60, 123);
        // Edges should be walls
        Assert.False(map.IsWalkable(0, 0));
        Assert.False(map.IsWalkable(-1, -1)); // Out of bounds
    }

    [Fact]
    public void TileMap_FindPlayerSpawn_ReturnsValidPosition()
    {
        var map = MapGenerator.Generate(80, 60, 42);
        var (x, y) = map.FindPlayerSpawn(new Random(1));
        Assert.True(map.IsWalkableF(x, y));
    }
}
