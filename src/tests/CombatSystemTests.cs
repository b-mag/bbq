using Carcosa.Server.Game;
using Xunit;

namespace Carcosa.Tests;

/// <summary>
/// Tests for CombatSystem — weapon firing and projectile creation.
/// </summary>
public class CombatSystemTests
{
    [Fact]
    public void ProcessPrimaryFire_Gangster_CreatesProjectiles()
    {
        var state = new GameState();
        state.Map = MapGenerator.Generate(40, 40, 1);

        var player = new Entity
        {
            Id = "player_1", Type = EntityType.Player, SubType = "gangster",
            X = 20, Y = 20, IsAlive = true, PrimaryFireCooldown = 0
        };
        state.AddEntity(player);

        CombatSystem.ProcessPrimaryFire(state, player, 0f);

        // Gangster fires 3 bullets per burst
        var projectiles = state.GetProjectiles().ToList();
        Assert.Equal(3, projectiles.Count);
    }

    [Fact]
    public void ProcessPrimaryFire_Detective_CreatesSingleProjectile()
    {
        var state = new GameState();
        state.Map = MapGenerator.Generate(40, 40, 1);

        var player = new Entity
        {
            Id = "player_1", Type = EntityType.Player, SubType = "detective",
            X = 20, Y = 20, IsAlive = true, PrimaryFireCooldown = 0
        };
        state.AddEntity(player);

        CombatSystem.ProcessPrimaryFire(state, player, 0f);

        var projectiles = state.GetProjectiles().ToList();
        Assert.Single(projectiles);
    }

    [Fact]
    public void ProcessPrimaryFire_RespectsCoooldown()
    {
        var state = new GameState();
        var player = new Entity
        {
            Id = "player_1", Type = EntityType.Player, SubType = "detective",
            X = 20, Y = 20, IsAlive = true, PrimaryFireCooldown = 5 // On cooldown
        };
        state.AddEntity(player);

        CombatSystem.ProcessPrimaryFire(state, player, 0f);

        // Should not fire while on cooldown
        Assert.Empty(state.GetProjectiles().ToList());
    }

    [Fact]
    public void ProcessSecondaryAbility_Surgeon_HealsAllies()
    {
        var state = new GameState();
        var healer = new Entity
        {
            Id = "healer", Type = EntityType.Player, SubType = "surgeon",
            X = 20, Y = 20, Health = 80, MaxHealth = 100, IsAlive = true,
            SecondaryAbilityCooldown = 0
        };
        var ally = new Entity
        {
            Id = "ally", Type = EntityType.Player, SubType = "gangster",
            X = 21, Y = 20, Health = 50, MaxHealth = 100, IsAlive = true
        };
        state.AddEntity(healer);
        state.AddEntity(ally);

        CombatSystem.ProcessSecondaryAbility(state, healer);

        // Both should be healed (surgeon heals self + allies in radius)
        Assert.True(healer.Health > 80);
        Assert.True(ally.Health > 50);
    }
}
