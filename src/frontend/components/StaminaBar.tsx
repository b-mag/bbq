/**
 * =============================================================================
 * StaminaBar.tsx — Stamina Bar (Below HP, Dark Souls-Style)
 * =============================================================================
 *
 * Displays the shared stamina bar that gates all abilities and sprinting.
 * Green when healthy, flashes red border when depleted (helpless state).
 * Positioned directly below the HP bar for the classic RPG look.
 *
 * VISUAL DESIGN:
 *   - Green fill = available stamina
 *   - Dark background = depleted portion
 *   - Red pulsing border = fully depleted (can't act)
 *   - Smooth transitions for responsive feel
 * =============================================================================
 */
'use client';

interface StaminaBarProps {
  stamina: number;
  maxStamina: number;
  isDepleted: boolean;
  shieldHp: number;
}

export default function StaminaBar({ stamina, maxStamina, isDepleted, shieldHp }: StaminaBarProps) {
  const percentage = maxStamina > 0 ? (stamina / maxStamina) * 100 : 0;

  // Color: green normally, yellow when low, with pulsing red border when depleted
  const barColor = percentage > 40 ? '#4a8c3f' : percentage > 20 ? '#8c8c3f' : '#8c6a3f';

  return (
    <div style={{
      position: 'absolute', top: 50, left: 12,
      display: 'flex', flexDirection: 'column', gap: 2,
    }}>
      {/* Stamina Bar container */}
      <div style={{
        width: 180, height: 12,
        background: 'rgba(10, 20, 10, 0.85)',
        border: `1px solid ${isDepleted ? '#c04040' : '#2a4a2a'}`,
        borderRadius: 2,
        position: 'relative',
        overflow: 'hidden',
        animation: isDepleted ? 'pulse-border 0.6s ease-in-out infinite alternate' : 'none',
      }}>
        {/* Stamina fill */}
        <div style={{
          width: `${percentage}%`,
          height: '100%',
          background: barColor,
          transition: 'width 0.08s ease-out',
          boxShadow: 'inset 0 1px 0 rgba(100,255,100,0.15)',
        }} />

        {/* Stamina text */}
        <div style={{
          position: 'absolute', top: 0, left: 0, right: 0, bottom: 0,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          fontSize: '0.55rem', color: '#c8e8c0',
          textShadow: '1px 1px 1px rgba(0,0,0,0.9)',
          fontFamily: 'sans-serif',
        }}>
          {Math.ceil(stamina)} / {Math.ceil(maxStamina)}
        </div>
      </div>

      {/* Shield indicator (shows when Iron Veil is active) */}
      {shieldHp > 0 && (
        <div style={{
          width: 180, height: 8,
          background: 'rgba(10, 10, 30, 0.85)',
          border: '1px solid #3f5f8c',
          borderRadius: 2,
          position: 'relative',
          overflow: 'hidden',
        }}>
          <div style={{
            width: `${(shieldHp / 25) * 100}%`, // 25 is max shield from Iron Veil
            height: '100%',
            background: '#3f6f9c',
            transition: 'width 0.1s ease-out',
          }} />
          <div style={{
            position: 'absolute', top: 0, left: 4, bottom: 0,
            display: 'flex', alignItems: 'center',
            fontSize: '0.5rem', color: '#a0c0e0',
          }}>
            Shield: {shieldHp}
          </div>
        </div>
      )}

      {/* Depleted warning */}
      {isDepleted && (
        <div style={{
          fontSize: '0.55rem', color: '#c04040',
          textShadow: '0 0 4px rgba(200,50,50,0.5)',
          fontFamily: 'sans-serif',
        }}>
          EXHAUSTED
        </div>
      )}
    </div>
  );
}
