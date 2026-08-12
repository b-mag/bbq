/**
 * =============================================================================
 * HealthBar.tsx — Player HP Bar (Top-Left HUD)
 * =============================================================================
 *
 * Displays current/max HP as a red bar with numeric value.
 * Positioned top-left, always visible during overworld gameplay.
 * Inspired by Link to the Past's simple, clear HP display.
 * =============================================================================
 */
'use client';

interface HealthBarProps {
  hp: number;
  maxHp: number;
  level: number;
}

export default function HealthBar({ hp, maxHp, level }: HealthBarProps) {
  const percentage = maxHp > 0 ? (hp / maxHp) * 100 : 0;

  // Color shifts from red to darker red as HP drops
  const barColor = percentage > 50 ? '#8c3f3f' : percentage > 25 ? '#a03030' : '#c02020';

  return (
    <div style={{
      position: 'absolute', top: 12, left: 12,
      display: 'flex', flexDirection: 'column', gap: 2,
    }}>
      {/* Level indicator */}
      <div style={{
        color: '#c9a84c', fontSize: '0.7rem', fontFamily: 'Georgia, serif',
        textShadow: '1px 1px 2px rgba(0,0,0,0.8)',
      }}>
        Lv. {level}
      </div>

      {/* HP Bar container */}
      <div style={{
        width: 180, height: 16,
        background: 'rgba(20, 10, 10, 0.85)',
        border: '1px solid #4a2020',
        borderRadius: 2,
        position: 'relative',
        overflow: 'hidden',
      }}>
        {/* HP fill */}
        <div style={{
          width: `${percentage}%`,
          height: '100%',
          background: barColor,
          transition: 'width 0.15s ease-out',
          boxShadow: 'inset 0 1px 0 rgba(255,100,100,0.2)',
        }} />

        {/* HP text overlay */}
        <div style={{
          position: 'absolute', top: 0, left: 0, right: 0, bottom: 0,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: '0.65rem', color: '#e8dcc8',
          textShadow: '1px 1px 1px rgba(0,0,0,0.9)',
          fontFamily: 'sans-serif',
        }}>
          {hp} / {maxHp}
        </div>
      </div>
    </div>
  );
}
