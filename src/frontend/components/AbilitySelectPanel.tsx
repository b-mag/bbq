/**
 * =============================================================================
 * AbilitySelectPanel.tsx — Meditation Altar Ability Selection
 * =============================================================================
 *
 * Shown when player interacts with a Meditation Altar (E key near altar).
 * Displays all 6 abilities in two columns (Primary / Secondary).
 * Player selects 1 of each and confirms.
 * =============================================================================
 */
'use client';

import { useState, useCallback } from 'react';

interface AbilitySelectPanelProps {
  currentPrimary: string;
  currentSecondary: string;
  onConfirm: (primary: string, secondary: string) => void;
  onClose: () => void;
}

interface AbilityInfo {
  id: string;
  name: string;
  type: string;
  cost: number;
  description: string;
  color: string;
}

const PRIMARY_ABILITIES: AbilityInfo[] = [
  { id: 'ember_spray', name: 'Ember Spray', type: 'Ranged AoE', cost: 25, description: 'Short-range cone of burning embers. Fast but stamina-hungry.', color: '#c06020' },
  { id: 'pale_blade', name: 'Pale Blade', type: 'Melee', cost: 20, description: 'Swift melee slash. Low cost, high damage, close range.', color: '#d0d0e0' },
  { id: 'void_bolt', name: 'Void Bolt', type: 'Ranged', cost: 22, description: 'Long-range projectile. High damage, slow cooldown.', color: '#6040a0' },
];

const SECONDARY_ABILITIES: AbilityInfo[] = [
  { id: 'warding_light', name: 'Warding Light', type: 'Heal AoE', cost: 35, description: 'Heal all nearby allies. Costly but powerful.', color: '#c0a040' },
  { id: 'iron_veil', name: 'Iron Veil', type: 'Shield', cost: 30, description: 'Temporary damage shield. Brief but strong.', color: '#4070a0' },
  { id: 'shadow_step', name: 'Shadow Step', type: 'Mobility', cost: 28, description: 'Short dash with invincibility frames.', color: '#303040' },
];

export default function AbilitySelectPanel({ currentPrimary, currentSecondary, onConfirm, onClose }: AbilitySelectPanelProps) {
  const [selectedPrimary, setSelectedPrimary] = useState(currentPrimary);
  const [selectedSecondary, setSelectedSecondary] = useState(currentSecondary);

  const handleConfirm = useCallback(() => {
    onConfirm(selectedPrimary, selectedSecondary);
  }, [selectedPrimary, selectedSecondary, onConfirm]);

  return (
    <div style={{
      position: 'absolute', inset: 0,
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      background: 'rgba(0, 0, 0, 0.6)',
      zIndex: 900,
    }}>
      <div style={{
        padding: '20px 24px',
        background: 'rgba(13, 15, 7, 0.97)',
        border: '1px solid #4a8c8c',
        borderRadius: 8,
        minWidth: 500,
      }}>
        {/* Header */}
        <h3 style={{
          color: '#6ac0c0', fontFamily: 'Georgia, serif',
          margin: '0 0 4px 0', fontSize: '1.1rem', textAlign: 'center',
        }}>
          Meditation Altar
        </h3>
        <p style={{ color: '#4a6a6a', fontSize: '0.7rem', textAlign: 'center', margin: '0 0 16px 0' }}>
          Choose your abilities. You may only change at another altar.
        </p>

        <div style={{ display: 'flex', gap: 20 }}>
          {/* Primary column */}
          <div style={{ flex: 1 }}>
            <div style={{ color: '#c9a84c', fontSize: '0.7rem', marginBottom: 8, fontWeight: 'bold' }}>
              PRIMARY (Left Click)
            </div>
            {PRIMARY_ABILITIES.map(ability => (
              <AbilityOption
                key={ability.id}
                ability={ability}
                isSelected={selectedPrimary === ability.id}
                isCurrent={currentPrimary === ability.id}
                onClick={() => setSelectedPrimary(ability.id)}
              />
            ))}
          </div>

          {/* Secondary column */}
          <div style={{ flex: 1 }}>
            <div style={{ color: '#c9a84c', fontSize: '0.7rem', marginBottom: 8, fontWeight: 'bold' }}>
              SECONDARY (Right Click)
            </div>
            {SECONDARY_ABILITIES.map(ability => (
              <AbilityOption
                key={ability.id}
                ability={ability}
                isSelected={selectedSecondary === ability.id}
                isCurrent={currentSecondary === ability.id}
                onClick={() => setSelectedSecondary(ability.id)}
              />
            ))}
          </div>
        </div>

        {/* Actions */}
        <div style={{ display: 'flex', gap: 12, justifyContent: 'center', marginTop: 16 }}>
          <button onClick={handleConfirm} style={{
            padding: '8px 24px', background: '#2a4a4a', border: '1px solid #6ac0c0',
            borderRadius: 4, color: '#6ac0c0', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            Confirm
          </button>
          <button onClick={onClose} style={{
            padding: '8px 24px', background: '#3a3520', border: '1px solid #6a5d4a',
            borderRadius: 4, color: '#6a5d4a', cursor: 'pointer', fontSize: '0.85rem',
            fontFamily: 'Georgia, serif',
          }}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}

function AbilityOption({ ability, isSelected, isCurrent, onClick }: {
  ability: AbilityInfo;
  isSelected: boolean;
  isCurrent: boolean;
  onClick: () => void;
}) {
  return (
    <div
      onClick={onClick}
      style={{
        padding: '8px 10px',
        marginBottom: 6,
        background: isSelected ? 'rgba(106, 192, 192, 0.1)' : 'rgba(20, 20, 15, 0.7)',
        border: `1.5px solid ${isSelected ? '#6ac0c0' : '#2a2a20'}`,
        borderRadius: 4,
        cursor: 'pointer',
        transition: 'border-color 0.15s',
      }}
    >
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span style={{ color: ability.color, fontWeight: 'bold', fontSize: '0.8rem' }}>
          {ability.name}
        </span>
        <span style={{ color: '#6a5d4a', fontSize: '0.6rem' }}>
          {ability.cost} stamina
        </span>
      </div>
      <div style={{ color: '#8a7a6a', fontSize: '0.6rem', marginTop: 2 }}>
        {ability.type} — {ability.description}
      </div>
      {isCurrent && (
        <div style={{ color: '#4a8c3f', fontSize: '0.55rem', marginTop: 2 }}>
          ● Currently equipped
        </div>
      )}
    </div>
  );
}
