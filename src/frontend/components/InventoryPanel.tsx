/**
 * =============================================================================
 * InventoryPanel.tsx — Player Inventory UI (Press I to Open)
 * =============================================================================
 *
 * Displays equipment slots, backpack grid, and ability loadout.
 * Loadout is locked while in a dungeon (equip + ability changes disabled).
 * =============================================================================
 */
'use client';

import { useEffect, useState, useCallback, type CSSProperties } from 'react';
import { PRIMARY_ABILITIES, SECONDARY_ABILITIES } from './AbilitySelectPanel';

interface InventorySlot {
  itemId: string | null;
  quantity: number;
  itemName: string | null;
  rarity: string | null;
  slot: string | null;
}

interface InventoryData {
  equipment: (InventorySlot | null)[];
  backpack: (InventorySlot | null)[];
}

const RARITY_COLORS: Record<string, string> = {
  Common: '#9a9a9a',
  Uncommon: '#4a8c3f',
  Rare: '#3f6f9c',
  Epic: '#8b5fbf',
};

const SLOT_LABELS = ['Weapon', 'Armor', 'Trinket', 'Boots'];

const selectStyle: CSSProperties = {
  width: '100%',
  background: '#1a1410',
  border: '1px solid #4a3d2e',
  borderRadius: 4,
  padding: '0.35rem 0.45rem',
  color: '#e8dcc8',
  fontSize: '0.7rem',
  fontFamily: 'Georgia, serif',
  outline: 'none',
};

interface InventoryPanelProps {
  onClose: () => void;
  loadoutLocked?: boolean;
  primaryAbility?: string;
  secondaryAbility?: string;
}

export default function InventoryPanel({
  onClose,
  loadoutLocked = false,
  primaryAbility = 'ember_spray',
  secondaryAbility = 'iron_veil',
}: InventoryPanelProps) {
  const [inventory, setInventory] = useState<InventoryData | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [primary, setPrimary] = useState(primaryAbility);
  const [secondary, setSecondary] = useState(secondaryAbility);

  useEffect(() => { setPrimary(primaryAbility); }, [primaryAbility]);
  useEffect(() => { setSecondary(secondaryAbility); }, [secondaryAbility]);

  const fetchInventory = useCallback(async () => {
    try {
      const res = await fetch('/api/gameplay/inventory');
      if (res.ok) {
        const data = await res.json();
        setInventory(data);
      }
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    fetchInventory();
    const interval = setInterval(fetchInventory, 500);
    return () => clearInterval(interval);
  }, [fetchInventory]);

  const showMsg = (text: string) => {
    setMessage(text);
    setTimeout(() => setMessage(null), 2000);
  };

  const handleEquip = useCallback(async (backpackSlot: number) => {
    if (loadoutLocked) {
      showMsg('Loadout locked in dungeon');
      return;
    }
    try {
      const res = await fetch('/api/gameplay/equip', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ backpackSlot }),
      });
      if (res.ok) {
        const result = await res.json();
        if (!result.success) {
          showMsg(result.message || 'Cannot equip');
        }
        fetchInventory();
      }
    } catch { /* ignore */ }
  }, [fetchInventory, loadoutLocked]);

  const handleAbilityChange = useCallback(async (nextPrimary: string, nextSecondary: string) => {
    if (loadoutLocked) {
      showMsg('Loadout locked in dungeon');
      return;
    }
    setPrimary(nextPrimary);
    setSecondary(nextSecondary);
    try {
      const res = await fetch('/api/gameplay/swap-abilities', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ primary: nextPrimary, secondary: nextSecondary }),
      });
      if (res.ok) {
        const result = await res.json();
        if (!result.success) {
          showMsg(result.message || 'Cannot change abilities');
          setPrimary(primaryAbility);
          setSecondary(secondaryAbility);
        }
      }
    } catch { /* ignore */ }
  }, [loadoutLocked, primaryAbility, secondaryAbility]);

  if (!inventory) return null;

  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.5)',
      zIndex: 900,
    }} onClick={onClose}>
      <div style={{
        padding: '16px 20px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #3a3520',
        borderRadius: 8,
        minWidth: 320,
      }} onClick={e => e.stopPropagation()}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          marginBottom: 12,
        }}>
          <h3 style={{ color: '#c9a84c', fontFamily: 'Georgia, serif', margin: 0, fontSize: '1rem' }}>
            Inventory
          </h3>
          <button onClick={onClose} style={{
            background: 'none', border: 'none', color: '#6a5d4a', cursor: 'pointer', fontSize: '1.2rem',
          }}>✕</button>
        </div>

        {loadoutLocked && (
          <div style={{
            color: '#c08050', fontSize: '0.7rem', textAlign: 'center',
            marginBottom: 10, padding: '4px 8px',
            border: '1px solid #6a4030', borderRadius: 4, background: 'rgba(60, 20, 10, 0.4)',
          }}>
            Loadout locked in dungeon
          </div>
        )}

        <div style={{ marginBottom: 12 }}>
          <div style={{ color: '#6a5d4a', fontSize: '0.65rem', marginBottom: 4 }}>EQUIPMENT</div>
          <div style={{ display: 'flex', gap: 6 }}>
            {SLOT_LABELS.map((label, i) => (
              <EquipSlot key={label} label={label} item={inventory.equipment[i]} />
            ))}
          </div>
        </div>

        <div style={{ marginBottom: 12 }}>
          <div style={{ color: '#6a5d4a', fontSize: '0.65rem', marginBottom: 4 }}>ABILITIES</div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
            <div>
              <div style={{ color: '#8a7a6a', fontSize: '0.55rem', marginBottom: 2 }}>Primary (LMB)</div>
              <select
                value={primary}
                disabled={loadoutLocked}
                onChange={e => handleAbilityChange(e.target.value, secondary)}
                style={{ ...selectStyle, opacity: loadoutLocked ? 0.5 : 1, cursor: loadoutLocked ? 'not-allowed' : 'pointer' }}
              >
                {PRIMARY_ABILITIES.map(a => (
                  <option key={a.id} value={a.id}>{a.name}</option>
                ))}
              </select>
            </div>
            <div>
              <div style={{ color: '#8a7a6a', fontSize: '0.55rem', marginBottom: 2 }}>Secondary (RMB)</div>
              <select
                value={secondary}
                disabled={loadoutLocked}
                onChange={e => handleAbilityChange(primary, e.target.value)}
                style={{ ...selectStyle, opacity: loadoutLocked ? 0.5 : 1, cursor: loadoutLocked ? 'not-allowed' : 'pointer' }}
              >
                {SECONDARY_ABILITIES.map(a => (
                  <option key={a.id} value={a.id}>{a.name}</option>
                ))}
              </select>
            </div>
          </div>
        </div>

        <div>
          <div style={{ color: '#6a5d4a', fontSize: '0.65rem', marginBottom: 4 }}>BACKPACK</div>
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 4 }}>
            {Array.from({ length: 12 }, (_, i) => (
              <BackpackSlot
                key={i}
                item={inventory.backpack[i]}
                locked={loadoutLocked}
                onClick={() => {
                  if (loadoutLocked) {
                    showMsg('Loadout locked in dungeon');
                    return;
                  }
                  if (inventory.backpack[i]?.slot && inventory.backpack[i]!.slot !== 'None') {
                    handleEquip(i);
                  }
                }}
              />
            ))}
          </div>
        </div>

        {message && (
          <div style={{ color: '#c04040', fontSize: '0.7rem', marginTop: 8, textAlign: 'center' }}>
            {message}
          </div>
        )}

        <div style={{ color: '#4a4030', fontSize: '0.6rem', marginTop: 8, textAlign: 'center' }}>
          Click equippable items to wear them. Press I or ESC to close.
        </div>
      </div>
    </div>
  );
}

function EquipSlot({ label, item }: { label: string; item: InventorySlot | null }) {
  const borderColor = item ? RARITY_COLORS[item.rarity || 'Common'] || '#3a3520' : '#2a2a20';

  return (
    <div style={{
      width: 60, display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 2,
    }}>
      <div style={{
        width: 48, height: 48,
        background: 'rgba(20, 20, 15, 0.9)',
        border: `2px solid ${borderColor}`,
        borderRadius: 4,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        fontSize: '0.55rem', color: item ? '#e8dcc8' : '#3a3520',
        textAlign: 'center', padding: 2,
      }}>
        {item ? item.itemName : '—'}
      </div>
      <div style={{ fontSize: '0.5rem', color: '#6a5d4a' }}>{label}</div>
    </div>
  );
}

function BackpackSlot({ item, onClick, locked }: {
  item: InventorySlot | null;
  onClick: () => void;
  locked: boolean;
}) {
  const borderColor = item ? RARITY_COLORS[item.rarity || 'Common'] || '#3a3520' : '#2a2a20';
  const isEquippable = item?.slot && item.slot !== 'None';

  return (
    <div
      onClick={onClick}
      style={{
        width: '100%', aspectRatio: '1', minHeight: 44,
        background: 'rgba(20, 20, 15, 0.9)',
        border: `1.5px solid ${borderColor}`,
        borderRadius: 3,
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        fontSize: '0.5rem', color: item ? '#c8c0b0' : '#2a2a20',
        textAlign: 'center', padding: 2,
        cursor: locked ? 'not-allowed' : (isEquippable ? 'pointer' : 'default'),
        opacity: locked ? 0.55 : 1,
        position: 'relative',
      }}
      title={item ? `${item.itemName} (${item.rarity})` : 'Empty'}
    >
      {item && (
        <>
          <span style={{ lineHeight: 1.1 }}>{item.itemName}</span>
          {item.quantity > 1 && (
            <span style={{ position: 'absolute', bottom: 2, right: 4, color: '#8a8a7a', fontSize: '0.5rem' }}>
              ×{item.quantity}
            </span>
          )}
        </>
      )}
    </div>
  );
}
