using Carcosa.Server.Game;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for the Entity class — damage, healing, and death mechanics.
/// </summary>
public class EntityTests
{
    [Fact]
    public void TakeDamage_ReducesHealth()
    {
        var entity = new Entity { Health = 100, MaxHealth = 100, IsAlive = true };
        entity.TakeDamage(30);
        Assert.Equal(70, entity.Health);
        Assert.True(entity.IsAlive);
    }

    [Fact]
    public void TakeDamage_KillsAtZero()
    {
        var entity = new Entity { Health = 10, MaxHealth = 100, IsAlive = true };
        var killed = entity.TakeDamage(10);
        Assert.True(killed);
        Assert.False(entity.IsAlive);
        Assert.Equal(0, entity.Health);
    }

    [Fact]
    public void TakeDamage_ClampsAtZero()
    {
        var entity = new Entity { Health = 5, MaxHealth = 100, IsAlive = true };
        entity.TakeDamage(999);
        Assert.Equal(0, entity.Health);
    }

    [Fact]
    public void Heal_RestoresHealth()
    {
        var entity = new Entity { Health = 50, MaxHealth = 100, IsAlive = true };
        entity.Heal(30);
        Assert.Equal(80, entity.Health);
    }

    [Fact]
    public void Heal_ClampsAtMax()
    {
        var entity = new Entity { Health = 90, MaxHealth = 100, IsAlive = true };
        entity.Heal(50);
        Assert.Equal(100, entity.Health);
    }

    [Fact]
    public void Heal_DoesNothingIfDead()
    {
        var entity = new Entity { Health = 0, MaxHealth = 100, IsAlive = false };
        entity.Heal(50);
        Assert.Equal(0, entity.Health);
    }
}
