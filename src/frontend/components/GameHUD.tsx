'use client';

import { EntityState, SessionInfoPayload, GameEventPayload } from '@/lib/messages';

interface GameHUDProps {
  localPlayerId: string | null;
  entities: EntityState[];
  sessionInfo: SessionInfoPayload | null;
  events: GameEventPayload[];
  latency: number;
  chatMessages: string[];
  chatInput: string;
  onChatInputChange: (val: string) => void;
  onChatSend: () => void;
  onChatFocus: () => void;
  onChatBlur: () => void;
  onDisconnect: () => void;
  children: React.ReactNode; // The game canvas
}

export default function GameHUD({
  localPlayerId,
  entities,
  sessionInfo,
  events,
  latency,
  chatMessages,
  chatInput,
  onChatInputChange,
  onChatSend,
  onChatFocus,
  onChatBlur,
  onDisconnect,
  children,
}: GameHUDProps) {
  const localEntity = entities.find(e => e.id === `player_${localPlayerId}`);
  const playerEntities = entities.filter(e => e.entityType === 'player');
  const enemyCount = entities.filter(e => e.entityType === 'enemy').length;
  const currentWave = sessionInfo?.currentWave ?? 0;

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
    }}>
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
                {localEntity.subType ? localEntity.subType.charAt(0).toUpperCase() + localEntity.subType.slice(1) : 'Unknown'}
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
                {localEntity.isAlive ? 'Active' : 'DOWNED - Need Revive!'}
              </div>
            </>
          )}
        </div>

        {/* Wave Info */}
        <div style={{ borderBottom: '1px solid #4a3d2e', paddingBottom: '0.5rem' }}>
          <div style={{ color: '#c9a84c', fontSize: '0.65rem', letterSpacing: '0.1em', textTransform: 'uppercase' }}>
            Mission
          </div>
          <div style={{ color: '#e8dcc8', fontSize: '0.8rem', marginTop: '0.2rem' }}>
            Wave {currentWave}/5
          </div>
          <div style={{ color: '#9a8b74', fontSize: '0.7rem' }}>
            {enemyCount} cultists remaining
          </div>
        </div>

        {/* Controls Reference */}
        <div style={{ fontSize: '0.6rem', color: '#6a5d4a' }}>
          <div style={{ color: '#9a8b74', fontSize: '0.65rem', marginBottom: '0.3rem' }}>Controls</div>
          <div>WASD — Move</div>
          <div>Click/Space — Attack</div>
          <div>E — Special Ability</div>
          <div>F — Revive Ally</div>
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
        border: '2px solid #4a3d2e',
        borderRadius: '4px',
        position: 'relative',
      }}>
        {children}
        {/* Game event overlay */}
        <GameEventOverlay events={events} />
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
          className={localEntity?.subType || ''}
          type="primary"
        />
        <AbilitySlot
          label="Special"
          hotkey="E"
          className={localEntity?.subType || ''}
          type="secondary"
        />
        <AbilitySlot
          label="Interact"
          hotkey="F"
          className=""
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
          <div style={{ display: 'flex', gap: '0.25rem', marginTop: '0.3rem' }}>
            <input
              type="text"
              placeholder="..."
              value={chatInput}
              onChange={(e) => onChatInputChange(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && onChatSend()}
              onFocus={onChatFocus}
              onBlur={onChatBlur}
              style={{
                flex: 1,
                background: '#0d0a07',
                border: '1px solid #4a3d2e',
                borderRadius: '3px',
                padding: '3px 5px',
                color: '#e8dcc8',
                fontSize: '0.65rem',
                outline: 'none',
              }}
            />
          </div>
        </div>
      </div>
    </div>
  );
}

// --- Sub-components ---

function AbilitySlot({ label, hotkey, className, type }: {
  label: string;
  hotkey: string;
  className: string;
  type: 'primary' | 'secondary' | 'interact';
}) {
  const getAbilityName = () => {
    if (type === 'interact') return 'Revive';
    if (type === 'primary') {
      switch (className) {
        case 'gangster': return 'Tommy Gun';
        case 'detective': return 'Magnum';
        case 'surgeon': return 'Dagger';
        default: return 'Attack';
      }
    }
    if (type === 'secondary') {
      switch (className) {
        case 'surgeon': return 'Group Heal';
        default: return '—';
      }
    }
    return '—';
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

function GameEventOverlay({ events }: { events: GameEventPayload[] }) {
  // Show the most recent game events as floating text
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
