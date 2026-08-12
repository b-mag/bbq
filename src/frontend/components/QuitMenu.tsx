/**
 * =============================================================================
 * QuitMenu.tsx — Quit/Disconnect Confirmation Dialog
 * =============================================================================
 *
 * Shown when ESC is pressed with no other panels open.
 * Replaces the visible disconnect button — cleaner UX.
 * Centered modal with dark fantasy styling.
 * =============================================================================
 */
'use client';

interface QuitMenuProps {
  onConfirm: () => void;
  onCancel: () => void;
}

export default function QuitMenu({ onConfirm, onCancel }: QuitMenuProps) {
  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.6)',
      zIndex: 1000,
    }}>
      <div style={{
        padding: '24px 36px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #4a3520',
        borderRadius: 8,
        textAlign: 'center',
        minWidth: 280,
      }}>
        <h3 style={{
          color: '#c9a84c', fontFamily: 'Georgia, serif',
          margin: '0 0 8px 0', fontSize: '1.1rem',
        }}>
          Exit game?
        </h3>
        <p style={{
          color: '#6a5d4a', fontSize: '0.8rem', margin: '0 0 20px 0',
        }}>
          Your progress has been saved.
        </p>
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center' }}>
          <button onClick={onConfirm} style={{
            padding: '8px 20px', background: '#4a2a2a', border: '1px solid #8c3f3f',
            borderRadius: 4, color: '#c05050', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            Disconnect
          </button>
          <button onClick={onCancel} style={{
            padding: '8px 20px', background: '#2a3a2a', border: '1px solid #4a8c3f',
            borderRadius: 4, color: '#4a8c3f', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}
