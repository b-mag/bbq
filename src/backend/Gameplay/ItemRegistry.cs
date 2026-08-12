// =============================================================================
// ItemRegistry.cs — Item Definitions & Database
// =============================================================================
//
// OVERVIEW:
// Static registry of all items that can drop from enemies, be equipped, or
// stored in inventory. Each item has a unique ID, rarity, equipment slot
// (if equippable), and stat modifiers.
//
// DESIGN PHILOSOPHY:
//   - Diablo-inspired: Gear modifies ability/combat stats directly
//   - Simple stat bonuses: +damage, +maxHP, +defense, -staminaCost, +regenRate
//   - Rarity tiers determine power level and visual distinction
//   - Items are identified by string ID (no procedural generation yet)
//   - Future: procedural affixes, set bonuses, unique effects
//
// EQUIPMENT SLOTS:
//   - Weapon: Modifies primary ability damage
//   - Armor: Modifies max HP and defense
//   - Trinket: Modifies secondary ability stats
//   - Boots: Modifies move speed and stamina regen
//   - Consumable: One-time use items (heal, buff)
//   - Material: Crafting materials (no use yet, stored for future)
//
// WHY STATIC REGISTRY:
// For the demo, all items are hardcoded. With 10 items, a database or JSON
// file would be overkill. The registry pattern allows easy expansion later
// (load from JSON, procedural generation, etc.).
// =============================================================================

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Item rarity tiers. Higher rarity = better stats and rarer drops.
/// Colors match the standard ARPG convention for instant visual recognition.
/// </summary>
public enum ItemRarity
{
    /// <summary>Common (white) — basic items, frequent drops.</summary>
    Common,
    /// <summary>Uncommon (green) — slightly better stats, moderate drop chance.</summary>
    Uncommon,
    /// <summary>Rare (blue) — significantly better, low drop chance.</summary>
    Rare,
    /// <summary>Epic (purple) — best items in current tier, very rare.</summary>
    Epic
}

/// <summary>
/// Which equipment slot an item occupies. Items with Slot=None are materials
/// or consumables that sit in the backpack but can't be equipped.
/// </summary>
public enum ItemSlot
{
    /// <summary>Not equippable — consumable or material.</summary>
    None,
    /// <summary>Weapon slot — modifies primary ability damage.</summary>
    Weapon,
    /// <summary>Armor slot — modifies max HP and defense.</summary>
    Armor,
    /// <summary>Trinket slot — modifies secondary ability stats.</summary>
    Trinket,
    /// <summary>Boots slot — modifies movement speed and stamina regen.</summary>
    Boots
}

/// <summary>
/// Stat modifiers applied when an item is equipped. All values are additive.
/// Zero values have no effect (most items only modify 1-2 stats).
/// </summary>
public sealed record ItemStatModifiers
{
    /// <summary>Added to primary ability damage.</summary>
    public int BonusDamage { get; init; }
    /// <summary>Added to max HP.</summary>
    public int BonusMaxHP { get; init; }
    /// <summary>Added to flat defense (damage reduction).</summary>
    public int BonusDefense { get; init; }
    /// <summary>Added to stamina regen rate (points/sec).</summary>
    public float BonusStaminaRegen { get; init; }
    /// <summary>Added to movement speed (tiles/sec).</summary>
    public float BonusMoveSpeed { get; init; }
    /// <summary>Subtracted from stamina cost of abilities (makes abilities cheaper).</summary>
    public float StaminaCostReduction { get; init; }
    /// <summary>Added to max stamina.</summary>
    public float BonusMaxStamina { get; init; }
    /// <summary>Added to heal amount (for healing abilities).</summary>
    public int BonusHealAmount { get; init; }
}

/// <summary>
/// Immutable definition of an item. Represents the "template" — individual
/// item instances in inventory reference this by ID.
/// </summary>
public sealed record ItemDefinition
{
    /// <summary>Unique item identifier (snake_case).</summary>
    public required string Id { get; init; }
    /// <summary>Display name (dark fantasy themed).</summary>
    public required string Name { get; init; }
    /// <summary>Brief description for tooltip.</summary>
    public required string Description { get; init; }
    /// <summary>Rarity tier (affects drop color and stat power).</summary>
    public required ItemRarity Rarity { get; init; }
    /// <summary>Equipment slot (None = can't equip).</summary>
    public required ItemSlot Slot { get; init; }
    /// <summary>Stat bonuses when equipped (null/zero for non-equipment).</summary>
    public ItemStatModifiers Stats { get; init; } = new();
    /// <summary>If true, consumed on use (removed from inventory).</summary>
    public bool IsConsumable { get; init; }
    /// <summary>Heal amount when consumed (consumables only).</summary>
    public int ConsumeHealAmount { get; init; }
    /// <summary>Stack limit for materials/consumables (1 = no stacking).</summary>
    public int MaxStack { get; init; } = 1;
}

/// <summary>
/// Static registry of all game items. Provides O(1) lookup by ID.
/// </summary>
public static class ItemRegistry
{
    private static readonly Dictionary<string, ItemDefinition> _items = new()
    {
        // =====================================================================
        // CONSUMABLES & MATERIALS (from Gronk drops)
        // =====================================================================

        ["raw_gronk_meat"] = new ItemDefinition
        {
            Id = "raw_gronk_meat",
            Name = "Raw Gronk Meat",
            Description = "Tough, gamey meat from a Gronk. Restores a small amount of health when consumed.",
            Rarity = ItemRarity.Common,
            Slot = ItemSlot.None,
            IsConsumable = true,
            ConsumeHealAmount = 15,
            MaxStack = 5,
        },

        ["dark_feathers"] = new ItemDefinition
        {
            Id = "dark_feathers",
            Name = "Dark Feathers",
            Description = "Iridescent black feathers. Useful for crafting or trade.",
            Rarity = ItemRarity.Common,
            Slot = ItemSlot.None,
            MaxStack = 10,
        },

        // =====================================================================
        // EQUIPMENT — WEAPONS (Modify Primary Ability)
        // =====================================================================

        ["dim_shore_blade"] = new ItemDefinition
        {
            Id = "dim_shore_blade",
            Name = "Dim Shore Blade",
            Description = "A rusted blade found near the docks. Still sharp enough to cut.",
            Rarity = ItemRarity.Common,
            Slot = ItemSlot.Weapon,
            Stats = new ItemStatModifiers { BonusDamage = 2 },
        },

        ["gronk_bone_knife"] = new ItemDefinition
        {
            Id = "gronk_bone_knife",
            Name = "Gronk Bone Knife",
            Description = "Carved from a Gronk's leg bone. Lightweight and surprisingly keen.",
            Rarity = ItemRarity.Uncommon,
            Slot = ItemSlot.Weapon,
            Stats = new ItemStatModifiers { BonusDamage = 4, StaminaCostReduction = 2f },
        },

        ["void_touched_wand"] = new ItemDefinition
        {
            Id = "void_touched_wand",
            Name = "Void-Touched Wand",
            Description = "Hums with eldritch energy. Projectiles travel further and hit harder.",
            Rarity = ItemRarity.Rare,
            Slot = ItemSlot.Weapon,
            Stats = new ItemStatModifiers { BonusDamage = 7, BonusMaxStamina = 15f },
        },

        // =====================================================================
        // EQUIPMENT — ARMOR (Modify HP and Defense)
        // =====================================================================

        ["tattered_hide"] = new ItemDefinition
        {
            Id = "tattered_hide",
            Name = "Tattered Hide",
            Description = "Gronk leather, poorly cured. Better than nothing.",
            Rarity = ItemRarity.Common,
            Slot = ItemSlot.Armor,
            Stats = new ItemStatModifiers { BonusMaxHP = 10, BonusDefense = 1 },
        },

        ["iron_scale_vest"] = new ItemDefinition
        {
            Id = "iron_scale_vest",
            Name = "Iron Scale Vest",
            Description = "Overlapping iron scales sewn onto leather. Heavy but protective.",
            Rarity = ItemRarity.Uncommon,
            Slot = ItemSlot.Armor,
            Stats = new ItemStatModifiers { BonusMaxHP = 20, BonusDefense = 3 },
        },

        // =====================================================================
        // EQUIPMENT — TRINKETS (Modify Secondary Ability)
        // =====================================================================

        ["gronk_bone_charm"] = new ItemDefinition
        {
            Id = "gronk_bone_charm",
            Name = "Gronk Bone Charm",
            Description = "A small charm carved from bone. Quickens stamina recovery.",
            Rarity = ItemRarity.Uncommon,
            Slot = ItemSlot.Trinket,
            Stats = new ItemStatModifiers { BonusStaminaRegen = 3f, BonusMaxStamina = 10f },
        },

        ["pale_ward_stone"] = new ItemDefinition
        {
            Id = "pale_ward_stone",
            Name = "Pale Ward Stone",
            Description = "A smooth stone that glows faintly. Enhances protective abilities.",
            Rarity = ItemRarity.Rare,
            Slot = ItemSlot.Trinket,
            Stats = new ItemStatModifiers { BonusHealAmount = 5, BonusMaxHP = 15, BonusStaminaRegen = 2f },
        },

        // =====================================================================
        // EQUIPMENT — BOOTS (Modify Speed and Stamina Regen)
        // =====================================================================

        ["worn_leather_boots"] = new ItemDefinition
        {
            Id = "worn_leather_boots",
            Name = "Worn Leather Boots",
            Description = "Cracked but functional. A slight spring in your step.",
            Rarity = ItemRarity.Common,
            Slot = ItemSlot.Boots,
            Stats = new ItemStatModifiers { BonusMoveSpeed = 0.3f, BonusStaminaRegen = 1f },
        },

        ["shadow_striders"] = new ItemDefinition
        {
            Id = "shadow_striders",
            Name = "Shadow Striders",
            Description = "Dark boots that seem to drink the light. Move faster, recover quicker.",
            Rarity = ItemRarity.Rare,
            Slot = ItemSlot.Boots,
            Stats = new ItemStatModifiers { BonusMoveSpeed = 0.7f, BonusStaminaRegen = 4f, BonusMaxStamina = 20f },
        },
    };

    // =========================================================================
    // PUBLIC API
    // =========================================================================

    /// <summary>Get an item definition by ID. Returns null if not found.</summary>
    public static ItemDefinition? GetItem(string id)
    {
        _items.TryGetValue(id, out var item);
        return item;
    }

    /// <summary>Check if an item ID exists.</summary>
    public static bool Exists(string id) => _items.ContainsKey(id);

    /// <summary>Get all items (for admin/debug).</summary>
    public static IEnumerable<ItemDefinition> GetAll() => _items.Values;

    /// <summary>Get all items of a specific slot type.</summary>
    public static IEnumerable<ItemDefinition> GetBySlot(ItemSlot slot) =>
        _items.Values.Where(i => i.Slot == slot);

    /// <summary>Get all items of a specific rarity.</summary>
    public static IEnumerable<ItemDefinition> GetByRarity(ItemRarity rarity) =>
        _items.Values.Where(i => i.Rarity == rarity);

    /// <summary>Total number of registered items.</summary>
    public static int Count => _items.Count;
}
