// =============================================================================
// AbilityRegistry.cs — Classless Ability Definitions (Pick 2 of 6)
// =============================================================================
//
// DESIGN PHILOSOPHY:
// Carcosa uses a CLASSLESS ability system inspired by:
//   - Dark Souls: Stamina-gated abilities, deliberate combat timing
//   - Diablo: Gear modifies ability stats, build diversity through loadout
//   - Zelda ALTTP: Simple but expressive abilities, clear visual language
//
// ABILITY SYSTEM RULES:
//   1. Players choose 1 Primary (LMB) + 1 Secondary (RMB) from a pool of 6
//   2. Abilities can only be swapped at Meditation Altars in the overworld
//   3. All abilities share the same stamina bar (no separate mana/energy)
//   4. Cooldowns are per-ability (can't spam even with full stamina)
//   5. Equipment modifies ability stats (+damage, -cost, +range, etc.)
//
// STARTING ABILITY POOL (6):
//   PRIMARY (offensive, Left Mouse Button):
//     - Ember Spray: Short-range cone of fire (area denial, crowd control)
//     - Pale Blade: Quick melee slash (fast, efficient, close range)
//     - Void Bolt: Single-target long-range projectile (sniper, high damage)
//
//   SECONDARY (utility/defensive, Right Mouse Button):
//     - Warding Light: Heal allies in radius (support, sustain)
//     - Iron Veil: Damage absorption shield (tank, face-tanking)
//     - Shadow Step: Short dash with i-frames (evasion, positioning)
//
// WHY STATIC REGISTRY:
// Ability definitions are immutable game data — loaded once, never modified.
// A static registry avoids DI complexity and provides O(1) lookup by ID.
// New abilities can be added by simply extending the dictionary.
//
// FUTURE EXPANSION:
// The registry pattern makes it trivial to add new abilities later (just add
// entries). Equipment stat modifiers are applied OVER the base values here,
// so the registry always represents unmodified base stats.
// =============================================================================

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Determines whether an ability occupies the Primary (LMB) or Secondary (RMB) slot.
/// Players must equip exactly 1 of each type.
/// </summary>
public enum AbilitySlot
{
    /// <summary>Primary ability — offensive, Left Mouse Button. Choose 1 of 3.</summary>
    Primary,
    /// <summary>Secondary ability — utility/defensive, Right Mouse Button. Choose 1 of 3.</summary>
    Secondary
}

/// <summary>
/// Categorizes ability behavior for the combat system's dispatch logic.
/// Each type has different processing: projectile creation, melee hit detection,
/// area-of-effect healing, shield application, or movement.
/// </summary>
public enum AbilityType
{
    /// <summary>Fires multiple projectiles in a spread cone (Ember Spray).</summary>
    RangedAoE,
    /// <summary>Instant melee hit in the aim direction (Pale Blade).</summary>
    Melee,
    /// <summary>Fires a single aimed projectile (Void Bolt).</summary>
    RangedSingle,
    /// <summary>Heals all allies within a radius (Warding Light).</summary>
    HealAoE,
    /// <summary>Applies a temporary damage-absorbing shield (Iron Veil).</summary>
    Shield,
    /// <summary>Short-range teleport/dash with invincibility frames (Shadow Step).</summary>
    Mobility
}

/// <summary>
/// Immutable definition of an ability's base stats. These values represent the
/// unmodified ability — equipment stat bonuses are applied on top at runtime.
/// 
/// WHY A RECORD: Records are immutable by default and provide value equality,
/// which is perfect for game data definitions. They also print nicely for debugging.
/// </summary>
public sealed record AbilityDefinition
{
    /// <summary>Unique ability identifier (snake_case). Used in Entity.PrimaryAbility/SecondaryAbility.</summary>
    public required string Id { get; init; }

    /// <summary>Human-readable display name (Carcosa dark fantasy themed).</summary>
    public required string Name { get; init; }

    /// <summary>Which slot this ability can be equipped in (Primary or Secondary).</summary>
    public required AbilitySlot Slot { get; init; }

    /// <summary>Behavioral category for combat system dispatch.</summary>
    public required AbilityType Type { get; init; }

    /// <summary>
    /// Stamina cost per use. This is the PRIMARY balance lever — higher cost means
    /// fewer uses before depletion. Range: 20-35 for current abilities.
    /// </summary>
    public required float StaminaCost { get; init; }

    /// <summary>Base damage per hit/projectile (0 for non-damage abilities like heal/shield).</summary>
    public int Damage { get; init; }

    /// <summary>
    /// Range in tiles. For projectiles: max travel distance. For melee: hit detection radius.
    /// For heal: effect radius. For dash: teleport distance.
    /// </summary>
    public float Range { get; init; }

    /// <summary>Cooldown in ticks (1 tick = 50ms) before ability can be used again.</summary>
    public int CooldownTicks { get; init; }

    /// <summary>Projectile speed in tiles per tick (only for RangedAoE and RangedSingle).</summary>
    public float ProjectileSpeed { get; init; }

    /// <summary>Area-of-effect radius in tiles (for HealAoE, RangedAoE cone width).</summary>
    public float AreaRadius { get; init; }

    /// <summary>Healing amount per target (only for HealAoE).</summary>
    public int HealAmount { get; init; }

    /// <summary>
    /// Duration in ticks for persistent effects (shield duration, i-frame duration).
    /// Iron Veil: 40 ticks (2s). Shadow Step i-frames: 10 ticks (0.5s).
    /// </summary>
    public int DurationTicks { get; init; }

    /// <summary>Number of projectiles fired per use (Ember Spray = 3, others = 1).</summary>
    public int ProjectileCount { get; init; } = 1;

    /// <summary>Spread angle in radians for multi-projectile abilities (Ember Spray cone).</summary>
    public float SpreadAngle { get; init; }

    /// <summary>Shield HP granted on use (only for Shield type — Iron Veil).</summary>
    public int ShieldAmount { get; init; }

    /// <summary>Flavor text description for the ability selection UI.</summary>
    public required string Description { get; init; }
}

/// <summary>
/// Static registry of all available abilities. Provides O(1) lookup by ID and
/// filtered queries by slot. Immutable after static initialization.
/// 
/// WHY NOT A DATABASE/FILE: For 6 abilities, hardcoded definitions are simpler,
/// faster, and AOT-friendly. No file I/O, no deserialization, no missing-file errors.
/// When the ability pool grows to 20+, we can migrate to a JSON data file.
/// </summary>
public static class AbilityRegistry
{
    // =========================================================================
    // ABILITY DEFINITIONS
    // =========================================================================

    private static readonly Dictionary<string, AbilityDefinition> _abilities = new()
    {
        // =====================================================================
        // PRIMARY ABILITIES (Left Mouse Button — offensive)
        // =====================================================================

        ["ember_spray"] = new AbilityDefinition
        {
            Id = "ember_spray",
            Name = "Ember Spray",
            Slot = AbilitySlot.Primary,
            Type = AbilityType.RangedAoE,
            StaminaCost = 25f,
            Damage = 4,                    // Per projectile (4 × 3 = 12 total if all hit)
            Range = 8f,                    // Short range — forces close engagement
            CooldownTicks = 4,             // 200ms — fast but stamina-hungry
            ProjectileSpeed = 0.5f,        // Tiles per tick (10 tiles/sec)
            ProjectileCount = 3,           // Three embers in a cone
            SpreadAngle = 0.52f,           // ~30 degree cone (π/6 radians)
            AreaRadius = 0f,               // Not area damage — individual projectiles
            Description = "Unleash a short-range cone of burning embers. Fast but stamina-hungry. Best against groups.",
        },

        ["pale_blade"] = new AbilityDefinition
        {
            Id = "pale_blade",
            Name = "Pale Blade",
            Slot = AbilitySlot.Primary,
            Type = AbilityType.Melee,
            StaminaCost = 20f,             // Cheapest primary — efficient but risky (close range)
            Damage = 12,                   // High single-hit damage
            Range = 1.5f,                  // Must be adjacent — high risk, high reward
            CooldownTicks = 6,             // 300ms — quick slashes
            ProjectileSpeed = 0f,          // Not a projectile
            Description = "A swift slash of pale steel. Low cost, high damage, but demands close quarters.",
        },

        ["void_bolt"] = new AbilityDefinition
        {
            Id = "void_bolt",
            Name = "Void Bolt",
            Slot = AbilitySlot.Primary,
            Type = AbilityType.RangedSingle,
            StaminaCost = 22f,
            Damage = 18,                   // Highest single-target damage
            Range = 15f,                   // Longest range — sniper playstyle
            CooldownTicks = 20,            // 1 second — can't spam, must aim carefully
            ProjectileSpeed = 0.7f,        // Tiles per tick (14 tiles/sec) — fast but dodgeable
            Description = "Hurl a bolt of void energy. Long range, devastating damage, but slow to recover.",
        },

        // =====================================================================
        // SECONDARY ABILITIES (Right Mouse Button — utility/defensive)
        // =====================================================================

        ["warding_light"] = new AbilityDefinition
        {
            Id = "warding_light",
            Name = "Warding Light",
            Slot = AbilitySlot.Secondary,
            Type = AbilityType.HealAoE,
            StaminaCost = 35f,             // Most expensive — powerful effect demands resource commitment
            Damage = 0,
            Range = 5f,                    // Used as heal radius
            CooldownTicks = 100,           // 5 seconds — significant commitment
            HealAmount = 15,               // Heals self + all allies in radius
            AreaRadius = 5f,               // Generous radius for party support
            Description = "Radiate healing light to all nearby allies. Costly but can turn the tide of battle.",
        },

        ["iron_veil"] = new AbilityDefinition
        {
            Id = "iron_veil",
            Name = "Iron Veil",
            Slot = AbilitySlot.Secondary,
            Type = AbilityType.Shield,
            StaminaCost = 30f,
            Damage = 0,
            CooldownTicks = 120,           // 6 seconds — can't permanently maintain shield
            ShieldAmount = 25,             // Absorbs 25 damage before breaking
            DurationTicks = 40,            // 2 seconds of protection
            Description = "Conjure a veil of iron mist that absorbs damage. Brief but powerful protection.",
        },

        ["shadow_step"] = new AbilityDefinition
        {
            Id = "shadow_step",
            Name = "Shadow Step",
            Slot = AbilitySlot.Secondary,
            Type = AbilityType.Mobility,
            StaminaCost = 28f,
            Damage = 0,
            Range = 3f,
            CooldownTicks = 40,
            DurationTicks = 10,
            Description = "Dissolve into shadow and reappear nearby. Brief invincibility during the step.",
        },

        ["bone_cleaver"] = new AbilityDefinition
        {
            Id = "bone_cleaver",
            Name = "Bone Cleaver",
            Slot = AbilitySlot.Primary,
            Type = AbilityType.Melee,
            StaminaCost = 28f,
            Damage = 20,
            Range = 2.0f,
            CooldownTicks = 14,
            Description = "A heavy bone-edged cleave. Slow, brutal, and made for tanking through packs.",
        },

        ["hex_dart"] = new AbilityDefinition
        {
            Id = "hex_dart",
            Name = "Hex Dart",
            Slot = AbilitySlot.Primary,
            Type = AbilityType.RangedSingle,
            StaminaCost = 18f,
            Damage = 10,
            Range = 12f,
            CooldownTicks = 10,
            ProjectileSpeed = 0.55f,
            Description = "A cursed dart that chips away at foes. Lower burst, steady pressure.",
        },

        ["grim_howl"] = new AbilityDefinition
        {
            Id = "grim_howl",
            Name = "Grim Howl",
            Slot = AbilitySlot.Secondary,
            Type = AbilityType.HealAoE,
            StaminaCost = 30f,
            Damage = 0,
            Range = 4f,
            HealAmount = 8,
            CooldownTicks = 40,
            Description = "A sustaining howl that knits allies' wounds. Modest heal, strong for group play.",
        },

        ["cinder_ward"] = new AbilityDefinition
        {
            Id = "cinder_ward",
            Name = "Cinder Ward",
            Slot = AbilitySlot.Secondary,
            Type = AbilityType.Shield,
            StaminaCost = 32f,
            Damage = 0,
            ShieldAmount = 20,
            DurationTicks = 50,
            CooldownTicks = 50,
            Description = "Wrap yourself in smoldering wards. Absorbs hits; sparks linger on the veil.",
        },
    };

    // =========================================================================
    // PUBLIC QUERY METHODS
    // =========================================================================

    /// <summary>
    /// Get an ability definition by ID. Returns null if the ID is not found.
    /// O(1) dictionary lookup.
    /// </summary>
    /// <param name="id">Ability ID (e.g., "ember_spray", "iron_veil").</param>
    public static AbilityDefinition? GetAbility(string id)
    {
        _abilities.TryGetValue(id, out var ability);
        return ability;
    }

    /// <summary>
    /// Get all Primary slot abilities (for ability selection UI).
    /// </summary>
    public static IEnumerable<AbilityDefinition> GetPrimaryAbilities()
    {
        return _abilities.Values.Where(a => a.Slot == AbilitySlot.Primary);
    }

    /// <summary>
    /// Get all Secondary slot abilities (for ability selection UI).
    /// </summary>
    public static IEnumerable<AbilityDefinition> GetSecondaryAbilities()
    {
        return _abilities.Values.Where(a => a.Slot == AbilitySlot.Secondary);
    }

    /// <summary>
    /// Get all registered abilities (for debug/admin panels).
    /// </summary>
    public static IEnumerable<AbilityDefinition> GetAll()
    {
        return _abilities.Values;
    }

    /// <summary>
    /// Check if an ability ID exists in the registry.
    /// </summary>
    public static bool Exists(string id)
    {
        return _abilities.ContainsKey(id);
    }

    /// <summary>
    /// Get the total number of registered abilities.
    /// </summary>
    public static int Count => _abilities.Count;
}
