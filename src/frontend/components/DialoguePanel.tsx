/**
 * Simple paged NPC dialogue. Edit copy in src/frontend/lib/npc-dialogue.ts.
 */
'use client';

import { NpcDialogue } from '@/lib/npc-dialogue';

interface DialoguePanelProps {
  dialogue: NpcDialogue;
  page: number;
  onAdvance: () => void;
  onClose: () => void;
}

export default function DialoguePanel({ dialogue, page, onAdvance, onClose }: DialoguePanelProps) {
  const line = dialogue.lines[Math.min(page, dialogue.lines.length - 1)] || '';
  const last = page >= dialogue.lines.length - 1;

  return (
    <div style={{
      position: 'absolute', bottom: 96, left: '50%', transform: 'translateX(-50%)',
      width: 'min(520px, 90vw)', padding: '16px 20px',
      background: 'rgba(13, 10, 6, 0.94)',
      border: '1px solid #C9A84C',
      borderRadius: 6,
      color: '#E8DCC8',
      zIndex: 40,
      fontFamily: 'Georgia, serif',
    }}>
      <div style={{ color: '#C9A84C', fontSize: '0.85rem', marginBottom: 8, letterSpacing: '0.04em' }}>
        {dialogue.name}
      </div>
      <div style={{ fontSize: '0.95rem', lineHeight: 1.45, minHeight: 48 }}>{line}</div>
      <div style={{ marginTop: 12, display: 'flex', justifyContent: 'space-between', fontSize: '0.75rem', color: '#9a8b74' }}>
        <button onClick={onClose} style={btnStyle}>Leave</button>
        <button onClick={last ? onClose : onAdvance} style={btnStyle}>
          {last ? 'E / Close' : 'E / Next'}
        </button>
      </div>
    </div>
  );
}

const btnStyle: React.CSSProperties = {
  background: 'transparent',
  border: '1px solid #6B3A28',
  color: '#E8D48B',
  padding: '4px 10px',
  cursor: 'pointer',
  borderRadius: 3,
};
