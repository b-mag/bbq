/**
 * Friends — mark connected mesh peers. Persisted to the save file.
 * Future mesh-split prefers keeping Friends in the same neighborhood.
 */
'use client';

import { useCallback, useEffect, useState, type CSSProperties } from 'react';

interface FriendRow {
  peerId: string;
  displayName: string;
}

interface ConnectedRow {
  peerId: string;
  displayName: string;
  latencyMs: number;
  isFriend: boolean;
}

interface FriendsPanelProps {
  onClose: () => void;
}

export default function FriendsPanel({ onClose }: FriendsPanelProps) {
  const [saved, setSaved] = useState<FriendRow[]>([]);
  const [connected, setConnected] = useState<ConnectedRow[]>([]);
  const [localId, setLocalId] = useState('');

  const refresh = useCallback(async () => {
    try {
      const res = await fetch('/api/gameplay/friends');
      if (!res.ok) return;
      const data = await res.json();
      setSaved(data.friends || []);
      setConnected(data.connected || []);
      setLocalId(data.localPeerId || '');
    } catch { /* ignore */ }
  }, []);

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 1500);
    return () => clearInterval(id);
  }, [refresh]);

  const toggle = async (peerId: string, displayName: string) => {
    try {
      await fetch('/api/gameplay/friends', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ peerId, displayName }),
      });
      await refresh();
    } catch { /* ignore */ }
  };

  const savedOnly = saved.filter(f => !connected.some(c => c.peerId === f.peerId));

  return (
    <div style={overlay} onClick={onClose}>
      <div style={panel} onClick={e => e.stopPropagation()}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'baseline', marginBottom: 8 }}>
          <h3 style={title}>Friends</h3>
          <button onClick={onClose} style={closeBtn}>✕</button>
        </div>
        <p style={sub}>
          Mark peers you would keep if the mesh ever splits. This list is written to your save —
          not to the mesh. Combat and loot still use Party, not Friends.
        </p>
        <div style={{ color: '#6a5d4a', fontSize: '0.65rem', marginBottom: 8 }}>
          You: {localId ? localId.slice(0, 8) : '—'}
        </div>

        <div style={section}>CONNECTED NOW</div>
        {connected.length === 0 && (
          <div style={empty}>No other peers on this mesh. Glyph or tracker will bring them.</div>
        )}
        {connected.map(c => (
          <div key={c.peerId} style={row}>
            <div>
              <div style={{ color: '#e8dcc8', fontSize: '0.85rem' }}>{c.displayName}</div>
              <div style={{ color: '#6a5d4a', fontSize: '0.6rem' }}>{c.peerId.slice(0, 8)} · {c.latencyMs}ms</div>
            </div>
            <button onClick={() => toggle(c.peerId, c.displayName)} style={c.isFriend ? friendOn : friendOff}>
              {c.isFriend ? 'Friend ✓' : 'Add Friend'}
            </button>
          </div>
        ))}

        {savedOnly.length > 0 && (
          <>
            <div style={{ ...section, marginTop: 14 }}>REMEMBERED (offline)</div>
            {savedOnly.map(f => (
              <div key={f.peerId} style={row}>
                <div>
                  <div style={{ color: '#c9a84c', fontSize: '0.85rem' }}>{f.displayName}</div>
                  <div style={{ color: '#6a5d4a', fontSize: '0.6rem' }}>{f.peerId.slice(0, 8)}</div>
                </div>
                <button onClick={() => toggle(f.peerId, f.displayName)} style={friendOn}>
                  Unfriend
                </button>
              </div>
            ))}
          </>
        )}
      </div>
    </div>
  );
}

const overlay: CSSProperties = {
  position: 'absolute', inset: 0, display: 'flex', alignItems: 'center', justifyContent: 'center',
  background: 'rgba(0, 0, 0, 0.55)', zIndex: 1010,
};
const panel: CSSProperties = {
  padding: '16px 20px', background: 'rgba(13, 15, 7, 0.97)', border: '1px solid #3a3520',
  borderRadius: 8, minWidth: 320, maxWidth: 440, width: '90vw',
};
const title: CSSProperties = { color: '#c9a84c', fontFamily: 'Georgia, serif', margin: 0, fontSize: '1rem' };
const sub: CSSProperties = { color: '#6a5d4a', fontSize: '0.7rem', margin: '0 0 12px 0', lineHeight: 1.4 };
const section: CSSProperties = { color: '#6a5d4a', fontSize: '0.6rem', letterSpacing: '0.08em', marginBottom: 6 };
const empty: CSSProperties = { color: '#4a4030', fontSize: '0.75rem', fontStyle: 'italic', marginBottom: 8 };
const row: CSSProperties = {
  display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: 10,
  padding: '8px 0', borderBottom: '1px solid #2a2218',
};
const closeBtn: CSSProperties = { background: 'none', border: 'none', color: '#6a5d4a', cursor: 'pointer', fontSize: '1.2rem' };
const friendOn: CSSProperties = {
  background: '#2a3a2a', border: '1px solid #4a8c3f', color: '#4a8c3f', borderRadius: 4,
  padding: '5px 10px', cursor: 'pointer', fontFamily: 'Georgia, serif', fontSize: '0.7rem',
};
const friendOff: CSSProperties = {
  background: '#2a2218', border: '1px solid #c9a84c', color: '#c9a84c', borderRadius: 4,
  padding: '5px 10px', cursor: 'pointer', fontFamily: 'Georgia, serif', fontSize: '0.7rem',
};
