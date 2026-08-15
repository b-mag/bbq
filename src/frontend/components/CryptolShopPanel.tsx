/**
 * Cryptol shop — only reachable in the Intact House when Lake Hali is drained.
 */
'use client';

import { useCallback, useEffect, useState } from 'react';

interface ShopItem {
  itemId: string;
  name: string;
  description: string;
  rarity: string;
  price: number;
}

interface ShopData {
  balance: number;
  items: ShopItem[];
}

interface CryptolShopPanelProps {
  onClose: () => void;
}

const RARITY: Record<string, string> = {
  Common: '#9a9a9a',
  Uncommon: '#4a8c3f',
  Rare: '#3f6f9c',
  Epic: '#8b5fbf',
};

export default function CryptolShopPanel({ onClose }: CryptolShopPanelProps) {
  const [shop, setShop] = useState<ShopData | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const res = await fetch('/api/gameplay/shop');
      if (res.ok) setShop(await res.json());
    } catch { /* ignore */ }
  }, []);

  useEffect(() => { load(); }, [load]);

  const buy = useCallback(async (itemId: string) => {
    if (busy) return;
    setBusy(true);
    try {
      const res = await fetch('/api/gameplay/shop/buy', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ itemId }),
      });
      const data = await res.json();
      setMessage(data.message || (data.success ? 'Purchased.' : 'The merchant shakes their head.'));
      await load();
    } catch {
      setMessage('The house does not answer.');
    }
    setBusy(false);
    setTimeout(() => setMessage(null), 2200);
  }, [busy, load]);

  return (
    <div style={{
      position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(8, 4, 2, 0.55)', zIndex: 50,
    }}>
      <div style={{
        width: 'min(480px, 92vw)', maxHeight: '80vh', overflow: 'auto',
        background: '#1A1208', border: '1px solid #C9A84C', borderRadius: 8,
        padding: 20, color: '#E8DCC8', fontFamily: 'Georgia, serif',
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 12 }}>
          <div>
            <div style={{ color: '#C9A84C', fontSize: '1.1rem' }}>The Intact House</div>
            <div style={{ color: '#9a8b74', fontSize: '0.8rem' }}>Cryptol is the only language spoken here.</div>
          </div>
          <div style={{ color: '#E8D48B' }}>{shop?.balance ?? 0} Cryptol</div>
        </div>
        {(shop?.items || []).map(item => (
          <div key={item.itemId} style={{
            display: 'flex', justifyContent: 'space-between', gap: 12,
            padding: '10px 0', borderTop: '1px solid #2E2214',
          }}>
            <div>
              <div style={{ color: RARITY[item.rarity] || '#E8DCC8' }}>{item.name}</div>
              <div style={{ color: '#8B6B2E', fontSize: '0.8rem' }}>{item.description}</div>
            </div>
            <button
              disabled={busy || (shop?.balance ?? 0) < item.price}
              onClick={() => buy(item.itemId)}
              style={{
                alignSelf: 'center', minWidth: 72,
                background: '#2E2214', border: '1px solid #8B6B2E',
                color: '#E8D48B', padding: '6px 10px', cursor: 'pointer',
                opacity: (shop?.balance ?? 0) < item.price ? 0.4 : 1,
              }}
            >
              {item.price}
            </button>
          </div>
        ))}
        {message && <div style={{ marginTop: 12, color: '#C9A84C', fontSize: '0.85rem' }}>{message}</div>}
        <button onClick={onClose} style={{
          marginTop: 16, background: 'transparent', border: '1px solid #6B3A28',
          color: '#A67C52', padding: '6px 12px', cursor: 'pointer',
        }}>Leave the counter</button>
      </div>
    </div>
  );
}
