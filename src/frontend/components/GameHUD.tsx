/**
 * =============================================================================
 * GameHUD.tsx — In-Game Heads-Up Display
 * =============================================================================
 *
 * WHY THIS LAYOUT:
 * The HUD uses CSS Grid to create a classic RPG layout:
 *   - Left panel: character stats, wave info, controls reference
 *   - Center: game canvas (passed as children)
 *   - Right panel: party health bars, chat log, pre-defined chat messages
 *   - Bottom bar: ability slots with hotkey labels
 *
 * PRE-DEFINED CHAT:
 * Instead of free-text chat, players select from 11 pre-defined messages.
 * This is opened with Enter key and messages are selected with number keys
 * (1-9, 0 for 10, - for 11) or by clicking. This keeps communication
 * family-friendly, fast during combat, and eliminates moderation needs.
 * =============================================================================
 */
'use client';

import { useState, useEffect, useCallback } from 'react';
import { EntityState, SessionInfoPayload, GameEventPayload } from '@/lib/messages';

/**
 * The 11 pre-defined chat messages available to players.
 * These cover the essential communication needs for cooperative gameplay:
 * status updates, tactical callouts, and social basics.
 */
export const PREDEFINED_MESSAGES = [
  'Hello',
  'Yes',
  'No',
  'Hold on afk for a moment',
  'Alright back',
  'You take the lead',
  'Lets pull them one at a time',
  'Is everyone ok?',
  'Wait here',
  "I'm low on ammo",
  "I'm hurt",
] as const;

interface GameHUDProps {
  localPlayerId: string | null;
  entities: EntityState[];
  sessionInfo: SessionInfoPayload | null;
  events: GameEventPayload[];
  latency: number;
  chatMessages: string[];
  /** Called when the player selects a pre-defined message to send. */
  onChatSend: (message: string) => void;
  /** Called when chat selector is open (disables game input). */
  onChatFocus: () => void;
  /** Called when chat selector closes (re-enables game input). */
  onChatBlur: () => void;
  onDisconnect: () => void;
  /** Whether the player is currently spectating (dead, watching teammates). */
  isSpectating?: boolean;
  /** Name/class of the player being spectated. */
  spectateTargetName?: string;
  /** Surrounding HUD chrome. DEV setting; default off. */
  showChrome?: boolean;
  children: React.ReactNode; // The game canvas
}

export default function GameHUD({
  localPlayerId,
  entities,
  sessionInfo: _sessionInfo,
  events,
  latency,
  chatMessages,
  onChatSend,
  onChatFocus,
  onChatBlur,
  onDisconnect,
  isSpectating = false,
  spectateTargetName,
  showChrome = false,
  children,
}: GameHUDProps) {
  const localEntity = entities.find(e => e.id === `player_${localPlayerId}`);
  const playerEntities = entities.filter(e => e.entityType === 'player');
  const enemyCount = entities.filter(e => e.entityType === 'enemy').length;

  // Chat selector open state — toggled by Enter key
  const [chatOpen, setChatOpen] = useState(false);

  // Handle keyboard shortcuts for chat
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      // Ignore if typing in an input element
      if (e.target instanceof HTMLInputElement || e.target instanceof HTMLTextAreaElement) return;

      // Enter toggles the chat selector
      if (e.key === 'Enter') {
        e.preventDefault();
        setChatOpen(prev => {
          const next = !prev;
          if (next) onChatFocus();
          else onChatBlur();
          return next;
        });
        return;
      }

      // Number keys select a message when chat is open
      if (chatOpen) {
        let index = -1;
        if (e.key >= '1' && e.key <= '9') {
          index = parseInt(e.key) - 1;
        } else if (e.key === '0') {
          index = 9; // 0 = 10th message
        } else if (e.key === '-') {
          index = 10; // - = 11th message
        } else if (e.key === 'Escape') {
          setChatOpen(false);
          onChatBlur();
          return;
        }

        if (index >= 0 && index < PREDEFINED_MESSAGES.length) {
          onChatSend(PREDEFINED_MESSAGES[index]);
          setChatOpen(false);
          onChatBlur();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [chatOpen, onChatSend, onChatFocus, onChatBlur]);

  const handleMessageClick = (message: string) => {
    onChatSend(message);
    setChatOpen(false);
    onChatBlur();
  };

  const canvasStage = (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      overflow: 'hidden',
      border: '2px solid #4a3d2e',
      borderRadius: '4px',
      position: 'relative',
      width: showChrome ? '100%' : 800,
      height: showChrome ? '100%' : 600,
    }}>
      {children}
      <GameEventOverlay events={events} />
      {isSpectating && (
        <div style={{
          position: 'absolute',
          bottom: '10px',
          left: '50%',
          transform: 'translateX(-50%)',
          background: 'rgba(26, 20, 16, 0.85)',
          border: '1px solid #4a3d2e',
          borderRadius: '4px',
          padding: '6px 16px',
          color: '#c9a84c',
          fontSize: '0.8rem',
          fontFamily: 'Georgia, serif',
          textAlign: 'center',
          pointerEvents: 'none',
        }}>
          SPECTATING: {spectateTargetName || 'Teammate'}
        </div>
      )}
      {chatOpen && (
        <ChatSelector
          onSelect={handleMessageClick}
          onClose={() => { setChatOpen(false); onChatBlur(); }}
        />
      )}
    </div>
  );

  if (!showChrome) {
    return (
      <div style={{
        height: '100vh',
        width: '100vw',
        background: '#0d0a07',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        position: 'relative',
        overflow: 'hidden',
      }} onContextMenu={e => e.preventDefault()}>
        {canvasStage}
        <button
          onClick={onDisconnect}
          style={{
            position: 'absolute',
            top: 12,
            right: 12,
            background: 'transparent',
            border: '1px solid #a83232',
            borderRadius: 3,
            padding: '4px 10px',
            color: '#a83232',
            cursor: 'pointer',
            fontSize: '0.7rem',
          }}
        >
          Leave
        </button>
      </div>
    );
  }

  return (
    <div style={{
      display: 'grid',
      gridTemplateColumns: '200px 1fr 200px',
      gridTemplateRows: '1fr 80px',
      gap: '4px',
      height: '100vh',
      width: '100vw',
      padding: '4px',
      background: '#0d0a07',
      overflow: 'hidden',
    }} onContextMenu={e => e.preventDefault()}>
      {/* Left Panel — Character Stats */}
      <div style={{
        gridRow: '1 / 3',
        background: '#1a1410',
        border: '1px solid #4a3d2e',
        borderRadius: '4px',
        padding: '0.75rem',
        display: 'flex',
        flexDirection: 'column',
        gap: '0.75rem',
        overflow: 'hidden',
      }}>
        {/* Character Info */}
        <div style={{ borderBottom: '1px solid #4a3d2e', paddingBottom: '0.5rem' }}>
          <div style={{
            color: '#c9a84c',
            fontSize: '0.7rem',
            letterSpacing: '0.15em',
            textTransform: 'uppercase',
            fontFamily: 'Georgia, serif',
          }}>
            Investigator
          </div>
          {localEntity && (
            <>
              <div style={{ color: '#e8dcc8', fontSize: '0.85rem', marginTop: '0.3rem' }}>
                Investigator
              </div>
              {/* HP Bar */}
              <div style={{ marginTop: '0.5rem' }}>
                <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '2px' }}>
                  <span style={{ color: '#9a8b74', fontSize: '0.65rem' }}>HP</span>
                  <span style={{ color: '#9a8b74', fontSize: '0.65rem' }}>
                    {localEntity.health}/{localEntity.maxHealth}
                  </span>
                </div>
                <div style={{ height: '8px', background: '#0d0a07', borderRadius: '2px', overflow: 'hidden' }}>
                  <div style={{
                    height: '100%',
                    width: `${(localEntity.health / localEntity.maxHealth) * 100}%`,
                    background: localEntity.health > 60 ? '#4a8c3f' :
                               localEntity.health > 30 ? '#c9a84c' : '#a83232',
                    transition: 'width 0.3s',
                    borderRadius: '2px',
                  }} />
                </div>
              </div>
              {/* Status */}
              <div style={{
                color: localEntity.isAlive ? '#4a8c3f' : '#a83232',
                fontSize: '0.7rem',
                marginTop: '0.3rem',
              }}>
                {localEntity.isAlive ? 'Active' : 'Returned to entrance'}
              </div>
              {/* Med Kits */}
              {localEntity.medKits > 0 && (
                <div style={{
                  color: '#e8dcc8',
                  fontSize: '0.65rem',
                  marginTop: '0.3rem',
                  display: 'flex',
                  alignItems: 'center',
                  gap: '0.3rem',
                }}>
                  <span style={{ color: '#4a8c3f' }}>+</span>
                  Med Kits: {localEntity.medKits}
                  <span style={{ color: '#6a5d4a', fontSize: '0.55rem' }}>[H]</span>
                </div>
              )}
            </>
          )}
        </div>

        {/* Wave Info */}
        <div style={{ borderBottom: '1px solid #4a3d2e', paddingBottom: '0.5rem' }}>
          <div style={{ color: '#c9a84c', fontSize: '0.65rem', letterSpacing: '0.1em', textTransform: 'uppercase' }}>
            Dungeon
          </div>
          <div style={{ color: '#e8dcc8', fontSize: '0.8rem', marginTop: '0.2rem' }}>
            {enemyCount} remaining
          </div>
          <div style={{ color: '#9a8b74', fontSize: '0.7rem' }}>
            Fixed encounter — no respawn
          </div>
        </div>

        {/* Controls Reference */}
        <div style={{ fontSize: '0.6rem', color: '#6a5d4a' }}>
          <div style={{ color: '#9a8b74', fontSize: '0.65rem', marginBottom: '0.3rem' }}>Controls</div>
          <div>WASD — Move</div>
          <div>LMB — Primary</div>
          <div>RMB — Secondary</div>
          <div>F — Interact</div>
          <div>H — Use Med Kit</div>
          <div>Enter — Chat</div>
        </div>

        {/* Connection Info */}
        <div style={{ marginTop: 'auto', fontSize: '0.6rem', color: '#6a5d4a' }}>
          <div>{latency}ms ping</div>
          <button
            onClick={onDisconnect}
            style={{
              marginTop: '0.3rem',
              background: 'transparent',
              border: '1px solid #a83232',
              borderRadius: '3px',
              padding: '2px 6px',
              color: '#a83232',
              cursor: 'pointer',
              fontSize: '0.6rem',
              width: '100%',
            }}
          >
            Leave Game
          </button>
        </div>
      </div>

      {/* Center — Game Canvas */}
      <div style={{
        gridColumn: '2',
        gridRow: '1',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        overflow: 'hidden',
      }}>
        {canvasStage}
      </div>

      {/* Bottom Bar — Abilities */}
      <div style={{
        gridColumn: '2',
        gridRow: '2',
        background: '#1a1410',
        border: '1px solid #4a3d2e',
        borderRadius: '4px',
        padding: '0.5rem',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: '1rem',
      }}>
        <AbilitySlot
          label="Primary"
          hotkey="LMB"
          abilityId={localEntity?.primaryAbility || ''}
          type="primary"
        />
        <AbilitySlot
          label="Special"
          hotkey="RMB"
          abilityId={localEntity?.secondaryAbility || ''}
          type="secondary"
        />
        <AbilitySlot
          label="Interact"
          hotkey="F"
          abilityId=""
          type="interact"
        />
      </div>

      {/* Right Panel — Party & Chat */}
      <div style={{
        gridRow: '1 / 3',
        background: '#1a1410',
        border: '1px solid #4a3d2e',
        borderRadius: '4px',
        padding: '0.75rem',
        display: 'flex',
        flexDirection: 'column',
        gap: '0.5rem',
        overflow: 'hidden',
      }}>
        {/* Party Members */}
        <div style={{ borderBottom: '1px solid #4a3d2e', paddingBottom: '0.5rem' }}>
          <div style={{
            color: '#c9a84c',
            fontSize: '0.65rem',
            letterSpacing: '0.1em',
            textTransform: 'uppercase',
            marginBottom: '0.3rem',
          }}>
            Party
          </div>
          {playerEntities.map(player => (
            <div key={player.id} style={{
              display: 'flex',
              alignItems: 'center',
              gap: '0.3rem',
              marginBottom: '0.3rem',
            }}>
              <div style={{
                width: '6px',
                height: '6px',
                borderRadius: '50%',
                background: player.isAlive ? '#4a8c3f' : '#a83232',
              }} />
              <div style={{ flex: 1 }}>
                <div style={{
                  color: player.id === `player_${localPlayerId}` ? '#c9a84c' : '#e8dcc8',
                  fontSize: '0.7rem',
                }}>
                  {player.subType || 'Player'}
                </div>
                <div style={{
                  height: '3px',
                  background: '#0d0a07',
                  borderRadius: '1px',
                  overflow: 'hidden',
                }}>
                  <div style={{
                    height: '100%',
                    width: `${(player.health / player.maxHealth) * 100}%`,
                    background: player.isAlive ? '#4a8c3f' : '#a83232',
                  }} />
                </div>
              </div>
            </div>
          ))}
        </div>

        {/* Chat Log */}
        <div style={{
          flex: 1,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}>
          <div style={{
            color: '#c9a84c',
            fontSize: '0.65rem',
            letterSpacing: '0.1em',
            textTransform: 'uppercase',
            marginBottom: '0.3rem',
          }}>
            Chat
          </div>
          <div style={{
            flex: 1,
            overflowY: 'auto',
            fontSize: '0.65rem',
            fontFamily: 'monospace',
          }}>
            {chatMessages.slice(-20).map((msg, i) => (
              <div key={i} style={{ color: '#9a8b74', marginBottom: '1px', wordBreak: 'break-word' }}>
                {msg}
              </div>
            ))}
          </div>
          {/* Quick chat hint */}
          <div style={{
            marginTop: '0.3rem',
            fontSize: '0.6rem',
            color: '#6a5d4a',
            textAlign: 'center',
            fontStyle: 'italic',
          }}>
            Press Enter to chat
          </div>
        </div>
      </div>
    </div>
  );
}

// =============================================================================
// Sub-components
// =============================================================================

/**
 * Chat message selector overlay — appears centered over the game canvas when
 * the player presses Enter. Shows all 11 pre-defined messages with number key
 * shortcuts for quick selection during combat.
 */
function ChatSelector({ onSelect, onClose }: {
  onSelect: (message: string) => void;
  onClose: () => void;
}) {
  return (
    <div
      style={{
        position: 'absolute',
        top: '50%',
        left: '50%',
        transform: 'translate(-50%, -50%)',
        background: 'rgba(26, 20, 16, 0.95)',
        border: '1px solid #4a3d2e',
        borderRadius: '8px',
        padding: '0.75rem',
        minWidth: '280px',
        zIndex: 100,
      }}
      onClick={(e) => e.stopPropagation()}
    >
      <div style={{
        color: '#c9a84c',
        fontSize: '0.75rem',
        marginBottom: '0.5rem',
        textAlign: 'center',
        fontFamily: 'Georgia, serif',
      }}>
        Quick Chat
      </div>
      <div style={{ display: 'flex', flexDirection: 'column', gap: '2px' }}>
        {PREDEFINED_MESSAGES.map((msg, i) => {
          // Hotkey label: 1-9, 0, -
          const hotkey = i < 9 ? `${i + 1}` : i === 9 ? '0' : '-';
          return (
            <button
              key={i}
              onClick={() => onSelect(msg)}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '0.5rem',
                background: '#2a2218',
                border: '1px solid #3a3020',
                borderRadius: '3px',
                padding: '4px 8px',
                cursor: 'pointer',
                textAlign: 'left',
                width: '100%',
                transition: 'border-color 0.15s',
              }}
              onMouseEnter={(e) => (e.currentTarget.style.borderColor = '#c9a84c')}
              onMouseLeave={(e) => (e.currentTarget.style.borderColor = '#3a3020')}
            >
              <span style={{
                color: '#c9a84c',
                fontSize: '0.6rem',
                fontFamily: 'monospace',
                minWidth: '16px',
                textAlign: 'center',
              }}>
                [{hotkey}]
              </span>
              <span style={{ color: '#e8dcc8', fontSize: '0.7rem' }}>
                {msg}
              </span>
            </button>
          );
        })}
      </div>
      <div style={{
        color: '#6a5d4a',
        fontSize: '0.6rem',
        marginTop: '0.5rem',
        textAlign: 'center',
      }}>
        Press number to send · Esc to close
      </div>
    </div>
  );
}

/**
 * Ability slot display in the bottom bar.
 * Shows the ability name and hotkey for each class's abilities.
 */
function AbilitySlot({ label: _label, hotkey, abilityId, type }: {
  label: string;
  hotkey: string;
  abilityId: string;
  type: 'primary' | 'secondary' | 'interact';
}) {
  const names: Record<string, string> = {
    ember_spray: 'Ember Spray',
    pale_blade: 'Pale Blade',
    void_bolt: 'Void Bolt',
    bone_cleaver: 'Bone Cleaver',
    hex_dart: 'Hex Dart',
    warding_light: 'Warding Light',
    iron_veil: 'Iron Veil',
    shadow_step: 'Shadow Step',
    grim_howl: 'Grim Howl',
    cinder_ward: 'Cinder Ward',
    soul_projection: 'Soul Projection',
  };
  const getAbilityName = () => {
    if (type === 'interact') return 'Interact';
    return names[abilityId] || (type === 'primary' ? 'Primary' : 'Secondary');
  };

  return (
    <div style={{
      background: '#0d0a07',
      border: '1px solid #4a3d2e',
      borderRadius: '4px',
      padding: '0.4rem 0.8rem',
      textAlign: 'center',
      minWidth: '80px',
    }}>
      <div style={{ color: '#e8dcc8', fontSize: '0.75rem' }}>{getAbilityName()}</div>
      <div style={{ color: '#6a5d4a', fontSize: '0.6rem', marginTop: '2px' }}>[{hotkey}]</div>
    </div>
  );
}

/**
 * Floating event overlay shown at the top center of the game canvas.
 * Displays wave announcements, victory/defeat messages, etc.
 */
function GameEventOverlay({ events }: { events: GameEventPayload[] }) {
  const recentEvents = events.slice(-3);

  if (recentEvents.length === 0) return null;

  return (
    <div style={{
      position: 'absolute',
      top: '10px',
      left: '50%',
      transform: 'translateX(-50%)',
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      gap: '4px',
      pointerEvents: 'none',
    }}>
      {recentEvents.map((event, i) => (
        <div key={i} style={{
          background: event.event === 'victory' ? 'rgba(74, 140, 63, 0.9)' :
                     event.event === 'game_over' ? 'rgba(168, 50, 50, 0.9)' :
                     event.event === 'wave_start' ? 'rgba(201, 168, 76, 0.9)' :
                     'rgba(26, 20, 16, 0.8)',
          border: '1px solid #4a3d2e',
          borderRadius: '4px',
          padding: '4px 12px',
          color: '#e8dcc8',
          fontSize: '0.8rem',
          fontFamily: 'Georgia, serif',
          textAlign: 'center',
          animation: 'fadeIn 0.3s ease',
        }}>
          {event.message || event.event}
        </div>
      ))}
    </div>
  );
}
