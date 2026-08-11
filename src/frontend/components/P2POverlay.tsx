/**
 * =============================================================================
 * P2POverlay.tsx — P2P Mesh Status & Controls Overlay
 * =============================================================================
 *
 * Displays P2P mesh information on the overworld view:
 *   - World shard indicator (e.g., "World: carcosa-01 (23/100)")
 *   - Glyph code (copy to share with friends)
 *   - Glyph input (enter a code to join a friend)
 *   - Peer count and mesh status
 *   - Admin broadcast messages (center-screen overlay)
 * =============================================================================
 */
'use client';

import { useState, useEffect } from 'react';
import { P2PStatus, ShardInfo } from '@/hooks/useP2POverworld';

interface P2POverlayProps {
  status: P2PStatus | null;
  shard: ShardInfo | null;
  glyph: string;
  onGlyphConnect: (code: string) => Promise<boolean>;
}

export default function P2POverlay({ status, shard, glyph, onGlyphConnect }: P2POverlayProps) {
  const [glyphInput, setGlyphInput] = useState('');
  const [glyphStatus, setGlyphStatus] = useState<'idle' | 'connecting' | 'success' | 'error'>('idle');
  const [showGlyphPanel, setShowGlyphPanel] = useState(false);
  const [copied, setCopied] = useState(false);
  const [adminMessage, setAdminMessage] = useState<string | null>(null);

  // Poll for admin messages
  useEffect(() => {
    const poll = async () => {
      try {
        const res = await fetch('/api/p2p/admin-messages');
        if (res.ok) {
          const data: Array<{ messageId: string; message: string; priority: string; durationSeconds: number; timestamp: number }> = await res.json();
          if (data.length > 0) {
            // Show the most recent message
            const latest = data[data.length - 1];
            if (latest.message !== adminMessage) {
              setAdminMessage(latest.message);
              // Auto-dismiss after duration
              setTimeout(() => setAdminMessage(null), (latest.durationSeconds || 15) * 1000);
            }
          }
        }
      } catch { /* Best effort */ }
    };
    const interval = setInterval(poll, 5000);
    poll();
    return () => clearInterval(interval);
  }, [adminMessage]);

  const handleGlyphConnect = async () => {
    if (!glyphInput.trim()) return;
    setGlyphStatus('connecting');
    const success = await onGlyphConnect(glyphInput.trim());
    setGlyphStatus(success ? 'success' : 'error');
    if (success) setGlyphInput('');
    setTimeout(() => setGlyphStatus('idle'), 3000);
  };

  const handleCopyGlyph = () => {
    if (!glyph || glyph === 'NO-ADDRESS-AVAILABLE') return;
    navigator.clipboard.writeText(glyph).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  };

  return (
    <>
      {/* Top-right: Shard & mesh info */}
      <div style={{
        position: 'absolute', top: 12, right: 12,
        display: 'flex', flexDirection: 'column', gap: 6, alignItems: 'flex-end',
      }}>
        {/* World shard badge */}
        {shard && (
          <div style={{
            padding: '4px 10px', background: 'rgba(13, 15, 7, 0.85)',
            border: '1px solid #3a3520', borderRadius: 4,
            fontSize: '0.7rem', color: '#9a9080',
          }}>
            <span style={{ color: '#c9a84c' }}>{shard.shardId}</span>
            {' '}({shard.playerCount}/{shard.maxPlayers})
          </div>
        )}

        {/* Peer count */}
        {status && (
          <div style={{
            padding: '4px 10px', background: 'rgba(13, 15, 7, 0.85)',
            border: '1px solid #3a3520', borderRadius: 4,
            fontSize: '0.65rem', color: '#6a5d4a',
          }}>
            Peers: {status.peerCount} | v{status.gameVersion}
          </div>
        )}

        {/* Glyph toggle button */}
        <button onClick={() => setShowGlyphPanel(!showGlyphPanel)} style={{
          padding: '4px 10px', background: 'rgba(13, 15, 7, 0.85)',
          border: '1px solid #4a3d2e', borderRadius: 4,
          fontSize: '0.65rem', color: '#c9a84c', cursor: 'pointer',
        }}>
          {showGlyphPanel ? 'Hide Glyph' : 'Glyph'}
        </button>
      </div>

      {/* Glyph panel (toggled) */}
      {showGlyphPanel && (
        <div style={{
          position: 'absolute', top: 80, right: 12, width: 260,
          background: 'rgba(13, 15, 7, 0.95)', border: '1px solid #4a3d2e',
          borderRadius: 8, padding: '12px', fontSize: '0.75rem',
        }}>
          {/* Our Glyph */}
          <div style={{ marginBottom: 10 }}>
            <div style={{ color: '#6a5d4a', marginBottom: 4 }}>Your Glyph (share with friends):</div>
            <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <code style={{
                flex: 1, padding: '4px 8px', background: '#1a1510',
                border: '1px solid #3a3520', borderRadius: 4,
                color: '#c9a84c', fontFamily: 'monospace', fontSize: '0.8rem',
                letterSpacing: '0.05em',
              }}>
                {glyph || '...'}
              </code>
              <button onClick={handleCopyGlyph} style={{
                padding: '4px 8px', background: '#2a2218',
                border: '1px solid #4a3d2e', borderRadius: 4,
                color: copied ? '#4a8c3f' : '#9a9080', cursor: 'pointer',
                fontSize: '0.65rem',
              }}>
                {copied ? 'Copied!' : 'Copy'}
              </button>
            </div>
          </div>

          {/* Join via Glyph */}
          <div>
            <div style={{ color: '#6a5d4a', marginBottom: 4 }}>Join a friend (enter their Glyph):</div>
            <div style={{ display: 'flex', gap: 6 }}>
              <input
                type="text"
                value={glyphInput}
                onChange={(e) => setGlyphInput(e.target.value.toUpperCase())}
                placeholder="HALI-DUSK-7A2F0"
                onKeyDown={(e) => e.key === 'Enter' && handleGlyphConnect()}
                style={{
                  flex: 1, padding: '4px 8px', background: '#1a1510',
                  border: '1px solid #3a3520', borderRadius: 4,
                  color: '#e8dcc8', fontFamily: 'monospace', fontSize: '0.75rem',
                  outline: 'none',
                }}
              />
              <button onClick={handleGlyphConnect} disabled={glyphStatus === 'connecting'} style={{
                padding: '4px 10px', borderRadius: 4, cursor: 'pointer',
                background: glyphStatus === 'success' ? '#2a4a2a' : glyphStatus === 'error' ? '#4a2a2a' : '#2a2218',
                border: `1px solid ${glyphStatus === 'success' ? '#4a8c3f' : glyphStatus === 'error' ? '#8c3f3f' : '#4a3d2e'}`,
                color: glyphStatus === 'success' ? '#4a8c3f' : glyphStatus === 'error' ? '#8c3f3f' : '#c9a84c',
                fontSize: '0.65rem',
              }}>
                {glyphStatus === 'connecting' ? '...' : glyphStatus === 'success' ? 'OK' : glyphStatus === 'error' ? 'Fail' : 'Join'}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Admin broadcast message (center screen overlay) */}
      {adminMessage && (
        <div style={{
          position: 'absolute', top: '20%', left: '50%', transform: 'translateX(-50%)',
          padding: '16px 32px', background: 'rgba(60, 30, 10, 0.95)',
          border: '2px solid #c9a84c', borderRadius: 8,
          color: '#e8dcc8', fontSize: '1.1rem', fontFamily: 'Georgia, serif',
          textAlign: 'center', maxWidth: '80%',
          animation: 'fadeIn 0.3s ease-in',
        }}>
          <div style={{ color: '#c9a84c', fontSize: '0.7rem', marginBottom: 6 }}>ADMIN MESSAGE</div>
          {adminMessage}
        </div>
      )}
    </>
  );
}
