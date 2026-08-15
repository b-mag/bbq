/**
 * =============================================================================
 * AbilityBar.tsx — Ability Slots Display (Bottom-Center HUD)
 * =============================================================================
 *
 * Shows the player's equipped Primary (LMB) and Secondary (RMB) abilities.
 * Each slot displays:
 *   - Colored icon square with ability initial
 *   - Ability name
 *   - Key binding hint (LMB / RMB)
 *   - Stamina cost
 *   - Cooldown overlay (grayed out when on cooldown)
 *   - Disabled state when stamina is insufficient
 * =============================================================================
 */
'use client';

interface AbilityBarProps {
  primaryAbility: string;
  secondaryAbility: string;
  primaryCooldown: number;
  secondaryCooldown: number;
  stamina: number;
  isDepleted: boolean;
}

// Ability display info mapping
const ABILITY_INFO: Record<string, { name: string; color: string; letter: string; cost: number }> = {
  ember_spray: { name: 'Ember Spray', color: '#c06020', letter: 'E', cost: 25 },
  pale_blade: { name: 'Pale Blade', color: '#d0d0e0', letter: 'P', cost: 20 },
  void_bolt: { name: 'Void Bolt', color: '#6040a0', letter: 'V', cost: 22 },
  bone_cleaver: { name: 'Bone Cleaver', color: '#a08060', letter: 'B', cost: 28 },
  hex_dart: { name: 'Hex Dart', color: '#8050a0', letter: 'H', cost: 18 },
  warding_light: { name: 'Warding Light', color: '#c0a040', letter: 'W', cost: 35 },
  iron_veil: { name: 'Iron Veil', color: '#4070a0', letter: 'I', cost: 30 },
  shadow_step: { name: 'Shadow Step', color: '#303040', letter: 'S', cost: 28 },
  grim_howl: { name: 'Grim Howl', color: '#906040', letter: 'G', cost: 30 },
  cinder_ward: { name: 'Cinder Ward', color: '#c05020', letter: 'C', cost: 32 },
  soul_projection: { name: 'Soul Projection', color: '#c8e8f0', letter: 'Ψ', cost: 26 },
};

export default function AbilityBar({
  primaryAbility, secondaryAbility,
  primaryCooldown, secondaryCooldown,
  stamina, isDepleted,
}: AbilityBarProps) {
  const primary = ABILITY_INFO[primaryAbility] || { name: 'None', color: '#444', letter: '?', cost: 0 };
  const secondary = ABILITY_INFO[secondaryAbility] || { name: 'None', color: '#444', letter: '?', cost: 0 };

  return (
    <div style={{
      position: 'absolute', bottom: 16, left: '50%', transform: 'translateX(-50%)',
      display: 'flex', gap: 12, alignItems: 'flex-end',
    }}>
      <AbilitySlot
        ability={primary}
        keybind="LMB"
        cooldown={primaryCooldown}
        stamina={stamina}
        isDepleted={isDepleted}
      />
      <AbilitySlot
        ability={secondary}
        keybind="RMB"
        cooldown={secondaryCooldown}
        stamina={stamina}
        isDepleted={isDepleted}
      />
    </div>
  );
}

function AbilitySlot({ ability, keybind, cooldown, stamina, isDepleted }: {
  ability: { name: string; color: string; letter: string; cost: number };
  keybind: string;
  cooldown: number;
  stamina: number;
  isDepleted: boolean;
}) {
  const isOnCooldown = cooldown > 0;
  const insufficientStamina = isDepleted || stamina < ability.cost;
  const isDisabled = isOnCooldown || insufficientStamina;

  return (
    <div style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center', gap: 3,
    }}>
      {/* Ability icon square */}
      <div style={{
        width: 44, height: 44,
        background: isDisabled ? 'rgba(40, 40, 40, 0.9)' : `rgba(20, 20, 20, 0.9)`,
        border: `2px solid ${isDisabled ? '#3a3a3a' : ability.color}`,
        borderRadius: 4,
        display: 'flex', alignItems: 'center', justifyContent: 'center',
        position: 'relative',
        overflow: 'hidden',
        opacity: isDisabled ? 0.6 : 1,
      }}>
        {/* Colored background tint */}
        <div style={{
          position: 'absolute', inset: 0,
          background: ability.color,
          opacity: isDisabled ? 0.1 : 0.25,
        }} />

        {/* Ability letter */}
        <span style={{
          fontSize: '1.2rem', fontWeight: 'bold',
          color: isDisabled ? '#666' : ability.color,
          fontFamily: 'Georgia, serif',
          position: 'relative',
          textShadow: `0 0 4px ${ability.color}40`,
        }}>
          {ability.letter}
        </span>

        {/* Cooldown overlay */}
        {isOnCooldown && (
          <div style={{
            position: 'absolute', inset: 0,
            background: 'rgba(0, 0, 0, 0.6)',
            display: 'flex', alignItems: 'center', justifyContent: 'center',
            fontSize: '0.7rem', color: '#aaa',
          }}>
            {(cooldown * 0.05).toFixed(1)}s
          </div>
        )}

        {/* Stamina cost badge */}
        <div style={{
          position: 'absolute', bottom: 1, right: 2,
          fontSize: '0.5rem',
          color: insufficientStamina ? '#c04040' : '#8ac080',
          fontFamily: 'sans-serif',
        }}>
          {ability.cost}
        </div>
      </div>

      {/* Ability name */}
      <div style={{
        fontSize: '0.55rem', color: '#9a8b74',
        fontFamily: 'sans-serif', textAlign: 'center',
        maxWidth: 60, lineHeight: 1.1,
      }}>
        {ability.name}
      </div>

      {/* Key binding hint */}
      <div style={{
        fontSize: '0.5rem', color: '#6a5d4a',
        background: 'rgba(13, 15, 7, 0.8)',
        padding: '1px 4px', borderRadius: 2,
        border: '1px solid #3a3520',
      }}>
        {keybind}
      </div>
    </div>
  );
}
