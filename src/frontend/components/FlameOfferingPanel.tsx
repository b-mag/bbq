/**
 * =============================================================================
 * FlameOfferingPanel.tsx — Meditation Altar (Offer to the Flame)
 * =============================================================================
 *
 * Lists backpack slots. Offering an item POST /api/gameplay/offer-to-flame
 * and shows Pale Marks gained.
 * =============================================================================
 */
'use client';

import { useCallback, useEffect, useState } from 'react';

interface InventorySlot {
  itemId: string | null;
  quantity: number;
  itemName: string | null;
  rarity: string | null;
  slot: string | null;
}

interface InventoryData {
  backpack: (InventorySlot | null)[];
}

const RARITY_COLORS: Record<string, string> = {
  Common: '#9a9a9a',
  Uncommon: '#4a8c3f',
  Rare: '#3f6f9c',
  Epic: '#8b5fbf',
};

interface FlameOfferingPanelProps {
  onClose: () => void;
}

export default function FlameOfferingPanel({ onClose }: FlameOfferingPanelProps) {
  const [backpack, setBackpack] = useState<(InventorySlot | null)[]>([]);
  const [selected, setSelected] = useState<number | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [offering, setOffering] = useState(false);

  const fetchInventory = useCallback(async () => {
    try {
      const res = await fetch('/api/gameplay/inventory');
      if (res.ok) {
        const data: InventoryData = await res.json();
        setBackpack(data.backpack || []);
      }
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    fetchInventory();
  }, [fetchInventory]);

  const handleOffer = useCallback(async () => {
    if (selected == null) {
      setMessage('Select an item to offer.');
      setTimeout(() => setMessage(null), 2000);
      return;
    }
    const item = backpack[selected];
    if (!item?.itemId) {
      setMessage('That slot is empty.');
      setTimeout(() => setMessage(null), 2000);
      return;
    }
    setOffering(true);
    try {
      const res = await fetch('/api/gameplay/offer-to-flame', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ backpackSlot: selected }),
      });
      if (res.ok) {
        const result = await res.json();
        if (result.success) {
          const marks = result.paleMarksGained ?? 0;
          setMessage(result.message || `+${marks} Pale Marks`);
          setSelected(null);
          fetchInventory();
        } else {
          setMessage(result.message || 'The flame rejects this offering.');
        }
      }
    } catch {
      setMessage('The flame is silent.');
    } finally {
      setOffering(false);
      setTimeout(() => setMessage(null), 3000);
    }
  }, [selected, backpack, fetchInventory]);

  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.55)',
      zIndex: 900,
    }} onClick={onClose}>
      <div style={{
        padding: '20px 24px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #6a4030',
        borderRadius: 8,
        minWidth: 320,
      }} onClick={e => e.stopPropagation()}>
        <div style={{
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
          marginBottom: 8,
        }}>
          <h3 style={{
            color: '#c08050', fontFamily: 'Georgia, serif', margin: 0, fontSize: '1.05rem',
          }}>
            Meditation Altar
          </h3>
          <button onClick={onClose} style={{
            background: 'none', border: 'none', color: '#6a5d4a', cursor: 'pointer', fontSize: '1.2rem',
          }}>✕</button>
        </div>
        <p style={{ color: '#6a5d4a', fontSize: '0.7rem', fontStyle: 'italic', margin: '0 0 14px 0' }}>
          Cast unwanted relics into the pale flame.
        </p>

        <div style={{ color: '#6a5d4a', fontSize: '0.65rem', marginBottom: 4 }}>BACKPACK</div>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: 4, marginBottom: 14 }}>
          {Array.from({ length: 12 }, (_, i) => {
            const item = backpack[i];
            const border = item
              ? (selected === i ? '#c9a84c' : (RARITY_COLORS[item.rarity || 'Common'] || '#3a3520'))
              : (selected === i ? '#c9a84c' : '#2a2a20');
            return (
              <div
                key={i}
                onClick={() => setSelected(i)}
                style={{
                  width: '100%', aspectRatio: '1', minHeight: 44,
                  background: selected === i ? 'rgba(60, 40, 16, 0.9)' : 'rgba(20, 20, 15, 0.9)',
                  border: `1.5px solid ${border}`,
                  borderRadius: 3,
                  display: 'flex', flexDirection: 'column',
                  alignItems: 'center', justifyContent: 'center',
                  fontSize: '0.5rem', color: item ? '#c8c0b0' : '#2a2a20',
                  textAlign: 'center', padding: 2, cursor: 'pointer', position: 'relative',
                }}
                title={item ? `${item.itemName} (${item.rarity})` : 'Empty'}
              >
                {item && (
                  <>
                    <span style={{ lineHeight: 1.1 }}>{item.itemName}</span>
                    {item.quantity > 1 && (
                      <span style={{
                        position: 'absolute', bottom: 2, right: 4,
                        color: '#8a8a7a', fontSize: '0.5rem',
                      }}>
                        ×{item.quantity}
                      </span>
                    )}
                  </>
                )}
              </div>
            );
          })}
        </div>

        <button
          onClick={handleOffer}
          disabled={offering}
          style={{
            width: '100%',
            padding: '10px 16px',
            background: '#3a2010',
            border: '1px solid #c08050',
            borderRadius: 4,
            color: '#c08050',
            cursor: offering ? 'wait' : 'pointer',
            fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
            letterSpacing: '0.04em',
          }}
        >
          {offering ? 'Offering…' : 'Offer to the Flame'}
        </button>

        {message && (
          <div style={{
            color: message.startsWith('+') || message.includes('Pale') ? '#c9a84c' : '#c05050',
            fontSize: '0.75rem', marginTop: 10, textAlign: 'center', fontFamily: 'Georgia, serif',
          }}>
            {message}
          </div>
        )}

        <div style={{ color: '#4a4030', fontSize: '0.6rem', marginTop: 10, textAlign: 'center' }}>
          Select a slot, then offer it. ESC to close.
        </div>
      </div>
    </div>
  );
}
