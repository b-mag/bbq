using Carcosa.Server.Game;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for GameState — entity management, dirty tracking, and queries.
/// </summary>
public class GameStateTests
{
    [Fact]
    public void AddEntity_CanBeRetrieved()
    {
        var state = new GameState();
        var entity = new Entity { Id = "test_1", Type = EntityType.Player, IsAlive = true };
        state.AddEntity(entity);

        Assert.True(state.Entities.ContainsKey("test_1"));
    }

    [Fact]
    public void RemoveEntity_RemovesIt()
    {
        var state = new GameState();
        state.AddEntity(new Entity { Id = "e1", Type = EntityType.Enemy });
        Assert.True(state.RemoveEntity("e1"));
        Assert.False(state.Entities.ContainsKey("e1"));
    }

    [Fact]
    public void GetPlayerByOwnerId_FindsCorrectPlayer()
    {
        var state = new GameState();
        state.AddEntity(new Entity { Id = "player_abc", Type = EntityType.Player, OwnerId = "abc" });
        state.AddEntity(new Entity { Id = "player_def", Type = EntityType.Player, OwnerId = "def" });

        var found = state.GetPlayerByOwnerId("def");
        Assert.NotNull(found);
        Assert.Equal("player_def", found.Id);
    }

    [Fact]
    public void GetAlivePlayers_ExcludesDead()
    {
        var state = new GameState();
        state.AddEntity(new Entity { Id = "p1", Type = EntityType.Player, IsAlive = true });
        state.AddEntity(new Entity { Id = "p2", Type = EntityType.Player, IsAlive = false });

        var alive = state.GetAlivePlayers().ToList();
        Assert.Single(alive);
        Assert.Equal("p1", alive[0].Id);
    }

    [Fact]
    public void GetDirtyEntities_OnlyReturnsDirty()
    {
        var state = new GameState();
        state.AddEntity(new Entity { Id = "e1", IsDirty = true });
        state.AddEntity(new Entity { Id = "e2", IsDirty = false });

        var dirty = state.GetDirtyEntities().ToList();
        Assert.Single(dirty);
        Assert.Equal("e1", dirty[0].Id);
    }

    [Fact]
    public void ClearDirtyFlags_ClearsAll()
    {
        var state = new GameState();
        state.AddEntity(new Entity { Id = "e1", IsDirty = true });
        state.AddEntity(new Entity { Id = "e2", IsDirty = true });

        state.ClearDirtyFlags();
        Assert.Empty(state.GetDirtyEntities().ToList());
    }
}
