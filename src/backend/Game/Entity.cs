namespace Carcosa.Server.Game;

/// <summary>
/// Type of entity in the game world.
/// </summary>
public enum EntityType
{
    Player,
    Enemy,
    Projectile
}

/// <summary>
/// Represents a game entity with position, velocity, and health.
/// This is the server-authoritative state for each entity in the world.
/// </summary>
public sealed class Entity
{
    public string Id { get; init; } = "";
    public EntityType Type { get; init; }
    public string SubType { get; set; } = ""; // e.g. "gangster", "detective", "surgeon", "cultist_acolyte"

    // Position (tile coordinates, float for sub-tile precision)
    public float X { get; set; }
    public float Y { get; set; }

    // Velocity (tiles per tick)
    public float VelocityX { get; set; }
    public float VelocityY { get; set; }

    // Health
    public int Health { get; set; } = 100;
    public int MaxHealth { get; set; } = 100;
    public bool IsAlive { get; set; } = true;

    // Movement speed (tiles per second)
    public float Speed { get; set; } = 5f;

    // For players: track which player owns this entity
    public string? OwnerId { get; set; }

    // For projectiles: track source and damage
    public string? SourceEntityId { get; set; }
    public int Damage { get; set; }
    public float Range { get; set; }
    public float DistanceTraveled { get; set; }

    // State tracking for delta updates
    public bool IsDirty { get; set; } = true;
    public int LastProcessedInput { get; set; }

    // Cooldowns (in ticks)
    public int PrimaryFireCooldown { get; set; }
    public int SecondaryAbilityCooldown { get; set; }

    /// <summary>
    /// Apply damage to this entity. Returns true if the entity died.
    /// </summary>
    public bool TakeDamage(int amount)
    {
        if (!IsAlive) return false;

        Health = Math.Max(0, Health - amount);
        IsDirty = true;

        if (Health <= 0)
        {
            IsAlive = false;
            VelocityX = 0;
            VelocityY = 0;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Heal this entity. Cannot exceed max health.
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsAlive) return;
        Health = Math.Min(MaxHealth, Health + amount);
        IsDirty = true;
    }
}
