/**
 * Key Items — permanent / non-consumable objects (Necronomicon, shovel, dug relics).
 * Press K, or open from Inventory.
 */
'use client';

import { useCallback, useEffect, useState, type CSSProperties } from 'react';

export interface KeyItem {
  itemId: string;
  name: string;
  description: string;
  rarity: string;
  usable: boolean;
}

interface KeyItemsPanelProps {
  onClose: () => void;
  onToast?: (msg: string) => void;
}

const RARITY: Record<string, string> = {
  Common: '#9a9a9a',
  Uncommon: '#4a8c3f',
  Rare: '#3f6f9c',
  Epic: '#8b5fbf',
};

export default function KeyItemsPanel({ onClose, onToast }: KeyItemsPanelProps) {
  const [items, setItems] = useState<KeyItem[]>([]);
  const [stage, setStage] = useState('');
  const [functions, setFunctions] = useState<string[]>([]);
  const [busy, setBusy] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    try {
      const res = await fetch('/api/gameplay/quest');
      if (!res.ok) return;
      const data = await res.json();
      setItems(data.keyItems || []);
      setStage(data.stage || '');
      setFunctions(data.necronomiconFunctions || []);
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 800);
    return () => clearInterval(id);
  }, [refresh]);

  const useItem = async (itemId: string) => {
    setBusy(itemId);
    try {
      const res = await fetch('/api/gameplay/key-items/use', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ itemId }),
      });
      const data = res.ok ? await res.json() : null;
      const msg = data?.message || 'Nothing happens.';
      onToast?.(msg);
      await refresh();
    } catch { /* ignore */ }
    setBusy(null);
  };

  return (
    <div style={overlay} onClick={onClose}>
      <div style={panel} onClick={e => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 10 }}>
          <h3 style={title}>Key Items</h3>
          <button onClick={onClose} style={closeBtn}>✕</button>
        </div>
        <p style={sub}>
          Permanent objects. They never occupy the backpack.
          {stage ? `  ·  Quest: ${stage}` : ''}
        </p>
        {functions.length > 0 && (
          <p style={{ ...sub, color: '#C9A84C' }}>
            Necronomicon: {functions.map(f => f.replace(/_/g, ' ')).join(', ')}
          </p>
        )}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 8, maxHeight: 360, overflowY: 'auto' }}>
          {items.length === 0 && (
            <div style={{ color: '#6a5d4a', fontStyle: 'italic', fontSize: '0.85rem', padding: 12 }}>
              The pockets that matter are empty. The sand may yet offer a husk.
            </div>
          )}
          {items.map(item => (
            <div key={item.itemId} style={{
              border: `1px solid ${RARITY[item.rarity] || '#3a3520'}`,
              borderRadius: 6,
              padding: '10px 12px',
              background: 'rgba(20, 16, 10, 0.85)',
            }}>
              <div style={{ display: 'flex', justifyContent: 'space-between', gap: 8 }}>
                <div>
                  <div style={{ color: RARITY[item.rarity] || '#e8dcc8', fontSize: '0.85rem' }}>{item.name}</div>
                  <div style={{ color: '#9a8b74', fontSize: '0.7rem', marginTop: 4, lineHeight: 1.35 }}>{item.description}</div>
                </div>
                {item.usable && (
                  <button
                    disabled={busy === item.itemId}
                    onClick={() => useItem(item.itemId)}
                    style={useBtn}
                  >
                    Use
                  </button>
                )}
              </div>
            </div>
          ))}
        </div>
        <div style={{ color: '#4a4030', fontSize: '0.6rem', marginTop: 10, textAlign: 'center' }}>
          K / ESC to close · Necronomicon Use = See Beyond (after pages)
        </div>
      </div>
    </div>
  );
}

const overlay: CSSProperties = {
  position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
  background: 'rgba(0, 0, 0, 0.55)', zIndex: 920,
};
const panel: CSSProperties = {
  padding: '16px 20px', background: 'rgba(13, 15, 7, 0.97)', border: '1px solid #3a3520',
  borderRadius: 8, minWidth: 340, maxWidth: 460, width: '90vw',
};
const title: CSSProperties = { color: '#c9a84c', fontFamily: 'Georgia, serif', margin: 0, fontSize: '1rem' };
const sub: CSSProperties = { color: '#6a5d4a', fontSize: '0.7rem', margin: '0 0 12px 0' };
const closeBtn: CSSProperties = { background: 'none', border: 'none', color: '#6a5d4a', cursor: 'pointer', fontSize: '1.2rem' };
const useBtn: CSSProperties = {
  background: '#2a2218', border: '1px solid #c9a84c', color: '#c9a84c', borderRadius: 4,
  padding: '6px 10px', cursor: 'pointer', fontFamily: 'Georgia, serif', fontSize: '0.75rem', height: 'fit-content',
};
