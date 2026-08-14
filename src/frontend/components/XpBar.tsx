/**
 * =============================================================================
 * XpBar.tsx — Thin XP Bar (Below HP/Stamina Cluster)
 * =============================================================================
 *
 * Gold fill showing progress toward the next level.
 * Positioned under the stamina bar: top ~68, left 12, 180×8.
 * =============================================================================
 */
'use client';

interface XpBarProps {
  xp: number;
  xpForNextLevel: number;
}

export default function XpBar({ xp, xpForNextLevel }: XpBarProps) {
  const max = xpForNextLevel > 0 ? xpForNextLevel : 1;
  const percentage = Math.max(0, Math.min(100, (xp / max) * 100));

  return (
    <div style={{
      position: 'absolute', top: 68, left: 12,
      width: 180, height: 8,
    }}>
      <div style={{
        width: 180, height: 8,
        background: 'rgba(20, 16, 8, 0.85)',
        border: '1px solid #4a3d2e',
        borderRadius: 2,
        position: 'relative',
        overflow: 'hidden',
      }}>
        <div style={{
          width: `${percentage}%`,
          height: '100%',
          background: '#c9a84c',
          transition: 'width 0.2s ease-out',
          boxShadow: 'inset 0 1px 0 rgba(255, 220, 140, 0.35)',
        }} />
      </div>
      <div style={{
        fontSize: '0.5rem', color: '#8a6d2f',
        fontFamily: 'sans-serif', marginTop: 1,
        textShadow: '1px 1px 1px rgba(0,0,0,0.8)',
      }}>
        {xp} / {xpForNextLevel}
      </div>
    </div>
  );
}
