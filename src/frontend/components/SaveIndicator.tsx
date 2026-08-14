/**
 * =============================================================================
 * SaveIndicator.tsx — Auto-Save Status Indicator (Top Center)
 * =============================================================================
 *
 * Polls player-stats lastSavedAt every 5s. When the timestamp changes,
 * shows a brief "Saving..." indicator for 1.5s.
 * =============================================================================
 */
'use client';

import { useState, useEffect, useRef } from 'react';

export default function SaveIndicator() {
  const [showSaving, setShowSaving] = useState(false);
  const lastSaveTime = useRef<string>('');

  useEffect(() => {
    const checkSave = async () => {
      try {
        const res = await fetch('/api/gameplay/player-stats');
        if (!res.ok) return;
        const data = await res.json();
        const stamp: string = data.lastSavedAt || '';
        if (!stamp) return;
        if (!lastSaveTime.current) {
          lastSaveTime.current = stamp;
          return;
        }
        if (stamp !== lastSaveTime.current) {
          lastSaveTime.current = stamp;
          setShowSaving(true);
          setTimeout(() => setShowSaving(false), 1500);
        }
      } catch { /* ignore */ }
    };

    checkSave();
    const interval = setInterval(checkSave, 5000);
    return () => clearInterval(interval);
  }, []);

  if (!showSaving) return null;

  return (
    <div style={{
      position: 'absolute', top: 8, left: '50%', transform: 'translateX(-50%)',
      display: 'flex', alignItems: 'center', gap: 6,
      padding: '4px 10px',
      background: 'rgba(13, 15, 7, 0.8)',
      border: '1px solid #3a3520',
      borderRadius: 4,
      zIndex: 500,
    }}>
      <div style={{
        width: 10, height: 10,
        border: '2px solid #4a3520',
        borderTop: '2px solid #c9a84c',
        borderRadius: '50%',
        animation: 'spin 0.8s linear infinite',
      }} />
      <span style={{ color: '#6a5d4a', fontSize: '0.6rem', fontFamily: 'sans-serif' }}>
        Saving...
      </span>
      <style>{`
        @keyframes spin {
          from { transform: rotate(0deg); }
          to { transform: rotate(360deg); }
        }
      `}</style>
    </div>
  );
}
