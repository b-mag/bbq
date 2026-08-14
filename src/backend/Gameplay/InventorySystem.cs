// =============================================================================
// InventorySystem.cs — Player Inventory & Equipment Management
// =============================================================================
//
// OVERVIEW:
// Manages the player's inventory: 4 equipment slots + 12 backpack slots.
// Equipment directly modifies Entity stats when equipped/unequipped.
//
// DESIGN:
//   - Equipment slots: Weapon, Armor, Trinket, Boots (1 item each)
//   - Backpack: 12 general slots (items stack based on MaxStack)
//   - Equipping: Move item from backpack to equipment slot (swap if occupied)
//   - Stat application: On equip, add stat modifiers to Entity. On unequip, remove.
//   - Thread safety: All mutations go through methods (no direct field access)
//
// WHY PER-PEER:
// Each peer manages their own inventory locally. The inventory is saved to disk
// (PlayerSave) and loaded on startup. No P2P sync of inventory needed — each
// player's items are private to them.
// =============================================================================

using Carcosa.Server.Game;

namespace Carcosa.Server.Gameplay;

/// <summary>
/// Represents a single item instance in the player's inventory.
/// References an ItemDefinition by ID and tracks stack count.
/// </summary>
public sealed class InventoryItem
{
    /// <summary>Item ID (references ItemRegistry).</summary>
    public required string ItemId { get; init; }
    /// <summary>Current stack count (1 for non-stackable items).</summary>
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// Manages a player's equipment and backpack inventory.
/// Provides equip/unequip/add/remove operations with stat application.
/// </summary>
public sealed class PlayerInventory
{
    // =========================================================================
    // CONSTANTS
    // =========================================================================

    /// <summary>Maximum backpack slots available.</summary>
    public const int BackpackSize = 12;

    // =========================================================================
    // FIELDS
    // =========================================================================

    /// <summary>Equipment slots: Weapon, Armor, Trinket, Boots.</summary>
    private readonly Dictionary<ItemSlot, InventoryItem?> _equipment = new()
    {
        [ItemSlot.Weapon] = null,
        [ItemSlot.Armor] = null,
        [ItemSlot.Trinket] = null,
        [ItemSlot.Boots] = null,
    };

    /// <summary>Backpack slots (fixed size array, null = empty slot).</summary>
    private readonly InventoryItem?[] _backpack = new InventoryItem?[BackpackSize];

    // =========================================================================
    // PROPERTIES
    // =========================================================================

    /// <summary>Get equipped item in a slot (null if empty).</summary>
    public InventoryItem? GetEquipped(ItemSlot slot) =>
        _equipment.TryGetValue(slot, out var item) ? item : null;

    /// <summary>Get all equipment slots as a dictionary (for serialization).</summary>
    public IReadOnlyDictionary<ItemSlot, InventoryItem?> Equipment => _equipment;

    /// <summary>Get backpack contents (for serialization).</summary>
    public IReadOnlyList<InventoryItem?> Backpack => _backpack;

    /// <summary>Number of occupied backpack slots.</summary>
    public int OccupiedSlots => _backpack.Count(s => s != null);

    /// <summary>Whether the backpack has any empty slots.</summary>
    public bool HasSpace => _backpack.Any(s => s == null);

    // =========================================================================
    // INVENTORY OPERATIONS
    // =========================================================================

    /// <summary>
    /// Add an item to the backpack. Tries to stack with existing items first,
    /// then uses an empty slot. Returns false if inventory is full.
    /// </summary>
    public bool AddItem(string itemId, int quantity = 1)
    {
        var itemDef = ItemRegistry.GetItem(itemId);
        if (itemDef == null) return false;

        // Try to stack with existing items of the same type
        if (itemDef.MaxStack > 1)
        {
            for (int i = 0; i < BackpackSize; i++)
            {
                if (_backpack[i] != null && _backpack[i]!.ItemId == itemId)
                {
                    int canAdd = itemDef.MaxStack - _backpack[i]!.Quantity;
                    if (canAdd > 0)
                    {
                        int toAdd = Math.Min(quantity, canAdd);
                        _backpack[i]!.Quantity += toAdd;
                        quantity -= toAdd;
                        if (quantity <= 0) return true;
                    }
                }
            }
        }

        // Place remaining quantity in empty slots
        while (quantity > 0)
        {
            int slotIndex = FindEmptySlot();
            if (slotIndex < 0) return false; // No space

            int stackSize = Math.Min(quantity, itemDef.MaxStack);
            _backpack[slotIndex] = new InventoryItem { ItemId = itemId, Quantity = stackSize };
            quantity -= stackSize;
        }

        return true;
    }

    /// <summary>
    /// Remove an item from a specific backpack slot. Returns the removed item.
    /// </summary>
    public InventoryItem? RemoveFromSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= BackpackSize) return null;
        var item = _backpack[slotIndex];
        _backpack[slotIndex] = null;
        return item;
    }

    /// <summary>
    /// Equip an item from backpack slot to its equipment slot.
    /// If the equipment slot is occupied, swaps the items.
    /// Applies/removes stat modifiers on the provided entity.
    /// Returns true if successful.
    /// </summary>
    public bool EquipFromBackpack(int backpackSlot, Entity entity)
    {
        if (backpackSlot < 0 || backpackSlot >= BackpackSize) return false;

        var item = _backpack[backpackSlot];
        if (item == null) return false;

        var itemDef = ItemRegistry.GetItem(item.ItemId);
        if (itemDef == null || itemDef.Slot == ItemSlot.None) return false;

        // Remove stat modifiers from currently equipped item (if any)
        var currentEquipped = _equipment[itemDef.Slot];
        if (currentEquipped != null)
        {
            var currentDef = ItemRegistry.GetItem(currentEquipped.ItemId);
            if (currentDef != null) RemoveStats(entity, currentDef.Stats);
        }

        // Swap: put current equipment into the backpack slot
        _backpack[backpackSlot] = currentEquipped;

        // Equip the new item
        _equipment[itemDef.Slot] = item;

        // Apply new item's stat modifiers
        ApplyStats(entity, itemDef.Stats);

        return true;
    }

    /// <summary>
    /// Unequip an item from an equipment slot into the backpack.
    /// Returns false if backpack is full (unless slot was already empty).
    /// </summary>
    public bool Unequip(ItemSlot slot, Entity entity)
    {
        var item = _equipment.GetValueOrDefault(slot);
        if (item == null) return true; // Already empty

        if (!HasSpace) return false; // No room in backpack

        // Remove stats
        var itemDef = ItemRegistry.GetItem(item.ItemId);
        if (itemDef != null) RemoveStats(entity, itemDef.Stats);

        // Move to backpack
        _equipment[slot] = null;
        int emptySlot = FindEmptySlot();
        if (emptySlot >= 0) _backpack[emptySlot] = item;

        return true;
    }

    /// <summary>
    /// Apply all currently equipped item stats to an entity.
    /// Called on game start after loading inventory from save.
    /// </summary>
    public void ApplyAllEquipmentStats(Entity entity)
    {
        foreach (var (_, item) in _equipment)
        {
            if (item == null) continue;
            var def = ItemRegistry.GetItem(item.ItemId);
            if (def != null) ApplyStats(entity, def.Stats);
        }
    }

    // =========================================================================
    // STAT APPLICATION
    // =========================================================================

    /// <summary>Apply stat modifiers from an item to an entity.</summary>
    private static void ApplyStats(Entity entity, ItemStatModifiers stats)
    {
        entity.Damage += stats.BonusDamage;
        entity.MaxHealth += stats.BonusMaxHP;
        entity.Health = Math.Min(entity.Health, entity.MaxHealth); // Don't exceed new max
        entity.Defense += stats.BonusDefense;
        entity.StaminaRegenRate += stats.BonusStaminaRegen;
        entity.Speed += stats.BonusMoveSpeed;
        entity.MaxStamina += stats.BonusMaxStamina;
        entity.StaminaCostReduction += stats.StaminaCostReduction;
        entity.BonusHealAmount += stats.BonusHealAmount;
    }

    /// <summary>Remove stat modifiers (reverse of ApplyStats).</summary>
    private static void RemoveStats(Entity entity, ItemStatModifiers stats)
    {
        entity.Damage -= stats.BonusDamage;
        entity.MaxHealth -= stats.BonusMaxHP;
        entity.Health = Math.Min(entity.Health, entity.MaxHealth);
        entity.Defense -= stats.BonusDefense;
        entity.StaminaRegenRate -= stats.BonusStaminaRegen;
        entity.Speed -= stats.BonusMoveSpeed;
        entity.MaxStamina -= stats.BonusMaxStamina;
        entity.StaminaCostReduction -= stats.StaminaCostReduction;
        entity.BonusHealAmount -= stats.BonusHealAmount;
    }

    // =========================================================================
    // SERIALIZATION HELPERS (for PlayerSave)
    // =========================================================================

    /// <summary>Get equipment as a simple dictionary of slot→itemId (for save).</summary>
    public Dictionary<string, string?> GetEquipmentForSave()
    {
        return new Dictionary<string, string?>
        {
            ["weapon"] = _equipment[ItemSlot.Weapon]?.ItemId,
            ["armor"] = _equipment[ItemSlot.Armor]?.ItemId,
            ["trinket"] = _equipment[ItemSlot.Trinket]?.ItemId,
            ["boots"] = _equipment[ItemSlot.Boots]?.ItemId,
        };
    }

    /// <summary>Get backpack as a list of (itemId, quantity) pairs (for save).</summary>
    public List<(string ItemId, int Quantity)?> GetBackpackForSave()
    {
        var result = new List<(string ItemId, int Quantity)?>();
        for (int i = 0; i < BackpackSize; i++)
        {
            result.Add(_backpack[i] != null ? (_backpack[i]!.ItemId, _backpack[i]!.Quantity) : null);
        }
        return result;
    }

    /// <summary>Load equipment from save data.</summary>
    public void LoadEquipment(Dictionary<string, string?>? data)
    {
        if (data == null) return;
        if (data.TryGetValue("weapon", out var w) && w != null)
            _equipment[ItemSlot.Weapon] = new InventoryItem { ItemId = w };
        if (data.TryGetValue("armor", out var a) && a != null)
            _equipment[ItemSlot.Armor] = new InventoryItem { ItemId = a };
        if (data.TryGetValue("trinket", out var t) && t != null)
            _equipment[ItemSlot.Trinket] = new InventoryItem { ItemId = t };
        if (data.TryGetValue("boots", out var b) && b != null)
            _equipment[ItemSlot.Boots] = new InventoryItem { ItemId = b };
    }

    /// <summary>Load backpack from save data.</summary>
    public void LoadBackpack(List<(string ItemId, int Quantity)?>? data)
    {
        if (data == null) return;
        for (int i = 0; i < Math.Min(data.Count, BackpackSize); i++)
        {
            if (data[i] != null)
            {
                _backpack[i] = new InventoryItem { ItemId = data[i]!.Value.ItemId, Quantity = data[i]!.Value.Quantity };
            }
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private int FindEmptySlot()
    {
        for (int i = 0; i < BackpackSize; i++)
        {
            if (_backpack[i] == null) return i;
        }
        return -1;
    }
}
