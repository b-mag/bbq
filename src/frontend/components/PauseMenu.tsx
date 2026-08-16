/**
 * =============================================================================
 * PauseMenu.tsx — ESC Pause Overlay
 * =============================================================================
 *
 * Shown when ESC is pressed with no other panels open.
 * Resume | Settings | Quit — dark fantasy modal.
 * =============================================================================
 */
'use client';

import type { CSSProperties } from 'react';

interface PauseMenuProps {
  onResume: () => void;
  onSettings: () => void;
  onFriends: () => void;
  onQuit: () => void;
}

const btnBase: CSSProperties = {
  width: '100%',
  padding: '10px 20px',
  borderRadius: 4,
  cursor: 'pointer',
  fontSize: '0.9rem',
  fontFamily: 'Georgia, serif',
  letterSpacing: '0.04em',
};

export default function PauseMenu({ onResume, onSettings, onFriends, onQuit }: PauseMenuProps) {
  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.6)',
      zIndex: 1000,
    }}>
      <div style={{
        padding: '28px 36px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #4a3520',
        borderRadius: 8,
        textAlign: 'center',
        minWidth: 280,
      }}>
        <h3 style={{
          color: '#c9a84c', fontFamily: 'Georgia, serif',
          margin: '0 0 6px 0', fontSize: '1.15rem', letterSpacing: '0.12em',
        }}>
          Paused
        </h3>
        <p style={{
          color: '#6a5d4a', fontSize: '0.75rem', margin: '0 0 20px 0', fontStyle: 'italic',
        }}>
          The twin suns hang still.
        </p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          <button onClick={onResume} style={{
            ...btnBase,
            background: '#2a3a2a', border: '1px solid #4a8c3f', color: '#4a8c3f',
          }}>
            Resume
          </button>
          <button onClick={onSettings} style={{
            ...btnBase,
            background: '#2a2218', border: '1px solid #c9a84c', color: '#c9a84c',
          }}>
            ⚙  Settings
          </button>
          <button onClick={onFriends} style={{
            ...btnBase,
            background: '#1a1820', border: '1px solid #6B3A9E', color: '#C9A8E0',
          }}>
            Friends
          </button>
          <button onClick={onQuit} style={{
            ...btnBase,
            background: '#4a2a2a', border: '1px solid #8c3f3f', color: '#c05050',
          }}>
            Quit
          </button>
        </div>
      </div>
    </div>
  );
}
