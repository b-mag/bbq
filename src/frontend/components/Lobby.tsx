'use client';

import { GameMessage, MessageTypes, SessionInfoPayload, SessionActionPayload } from '@/lib/messages';

interface LobbyProps {
  sessionInfo: SessionInfoPayload;
  localPlayerId: string | null;
  send: (msg: GameMessage) => void;
}

const CLASS_INFO = {
  gangster: {
    name: 'Gangster',
    color: '#8b4513',
    description: 'Tommy gun • Spray fire • Long range, low accuracy',
    detail: 'Magazine: 50 rounds | Damage: 2/bullet | Range: 15 tiles',
  },
  detective: {
    name: 'Detective',
    color: '#2f4f4f',
    description: 'Magnum • Single shot • Slow but powerful',
    detail: 'Cooldown: 1.5s | Damage: 25 | Range: 20 tiles | Accuracy: 90%',
  },
  surgeon: {
    name: 'Surgeon',
    color: '#f5f5dc',
    description: 'Dagger • Group Heal • Support class',
    detail: 'Melee: 8 dmg | Heal: 15hp (5 tile radius, 10s CD)',
  },
};

export default function Lobby({ sessionInfo, localPlayerId, send }: LobbyProps) {
  const localPlayer = sessionInfo.players.find(p => p.id === localPlayerId);
  const isHost = localPlayer?.isHost ?? false;
  const allReady = sessionInfo.players.length > 0 && sessionInfo.players.every(p => p.isReady);

  const sendAction = (action: SessionActionPayload['action'], value?: string) => {
    send({
      type: MessageTypes.SessionAction,
      sessionAction: { action, value },
    } as GameMessage);
  };

  return (
    <main style={{
      display: 'flex',
      flexDirection: 'column',
      alignItems: 'center',
      justifyContent: 'center',
      height: '100vh',
      width: '100vw',
      background: 'radial-gradient(ellipse at center, #2a2218 0%, #1a1410 70%, #0d0a07 100%)',
      padding: '2rem',
      gap: '1.5rem',
    }}>
      <h1 style={{
        fontSize: '2.5rem',
        fontFamily: "'Georgia', serif",
        color: '#c9a84c',
        textShadow: '0 0 20px rgba(201, 168, 76, 0.3)',
        letterSpacing: '0.2em',
      }}>
        CARCOSA
      </h1>
      <p style={{ color: '#9a8b74', fontSize: '0.9rem' }}>
        Lobby • {sessionInfo.players.length}/{sessionInfo.maxPlayers} players
      </p>

      {/* Class Selection */}
      <div style={{
        display: 'flex',
        gap: '1rem',
        flexWrap: 'wrap',
        justifyContent: 'center',
      }}>
        {(Object.entries(CLASS_INFO) as [string, typeof CLASS_INFO.gangster][]).map(([key, info]) => {
          const isSelected = localPlayer?.selectedClass === key;
          return (
            <button
              key={key}
              onClick={() => sendAction('select_class', key)}
              style={{
                background: isSelected ? info.color : '#2a2218',
                border: `2px solid ${isSelected ? '#c9a84c' : '#4a3d2e'}`,
                borderRadius: '8px',
                padding: '1rem',
                width: '180px',
                cursor: 'pointer',
                textAlign: 'left',
                transition: 'border-color 0.2s',
              }}
            >
              <div style={{
                color: isSelected ? '#fff' : '#c9a84c',
                fontSize: '1rem',
                fontFamily: 'Georgia, serif',
                fontWeight: 'bold',
                marginBottom: '0.3rem',
              }}>
                {info.name}
              </div>
              <div style={{
                color: isSelected ? '#ddd' : '#9a8b74',
                fontSize: '0.75rem',
                marginBottom: '0.3rem',
              }}>
                {info.description}
              </div>
              <div style={{
                color: isSelected ? '#bbb' : '#6a5d4a',
                fontSize: '0.65rem',
                fontFamily: 'monospace',
              }}>
                {info.detail}
              </div>
            </button>
          );
        })}
      </div>

      {/* Player List */}
      <div style={{
        background: '#1a1410',
        border: '1px solid #4a3d2e',
        borderRadius: '8px',
        padding: '1rem',
        width: '100%',
        maxWidth: '500px',
      }}>
        <div style={{ color: '#c9a84c', fontSize: '0.8rem', marginBottom: '0.5rem', fontFamily: 'Georgia, serif' }}>
          Players
        </div>
        {sessionInfo.players.map(player => (
          <div key={player.id} style={{
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
            padding: '0.4rem 0',
            borderBottom: '1px solid #2a2218',
          }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
              <span style={{ color: '#e8dcc8', fontSize: '0.85rem' }}>
                {player.isHost ? '👑 ' : ''}{player.name}
              </span>
              {player.selectedClass && (
                <span style={{
                  color: CLASS_INFO[player.selectedClass as keyof typeof CLASS_INFO]?.color || '#666',
                  fontSize: '0.7rem',
                  background: '#2a2218',
                  padding: '1px 6px',
                  borderRadius: '3px',
                }}>
                  {player.selectedClass}
                </span>
              )}
            </div>
            <span style={{
              color: player.isReady ? '#4a8c3f' : '#6a5d4a',
              fontSize: '0.75rem',
            }}>
              {player.isReady ? '✓ Ready' : '...'}
            </span>
          </div>
        ))}
      </div>

      {/* Ready / Start buttons */}
      <div style={{ display: 'flex', gap: '1rem' }}>
        {localPlayer && !localPlayer.isReady && (
          <button
            onClick={() => sendAction('set_ready', 'true')}
            disabled={!localPlayer.selectedClass}
            style={{
              background: localPlayer.selectedClass ? '#2a4a2a' : '#2a2218',
              border: `1px solid ${localPlayer.selectedClass ? '#4a8c3f' : '#4a3d2e'}`,
              borderRadius: '6px',
              padding: '0.6rem 1.5rem',
              color: localPlayer.selectedClass ? '#4a8c3f' : '#6a5d4a',
              cursor: localPlayer.selectedClass ? 'pointer' : 'not-allowed',
              fontSize: '0.9rem',
              fontFamily: 'Georgia, serif',
            }}
          >
            Ready Up
          </button>
        )}
        {localPlayer?.isReady && !isHost && (
          <button
            onClick={() => sendAction('set_ready', 'false')}
            style={{
              background: '#2a2218',
              border: '1px solid #c9a84c',
              borderRadius: '6px',
              padding: '0.6rem 1.5rem',
              color: '#c9a84c',
              cursor: 'pointer',
              fontSize: '0.9rem',
              fontFamily: 'Georgia, serif',
            }}
          >
            Cancel Ready
          </button>
        )}
        {isHost && (
          <button
            onClick={() => sendAction('start_game')}
            disabled={!allReady}
            style={{
              background: allReady ? '#4a3d2e' : '#2a2218',
              border: `1px solid ${allReady ? '#c9a84c' : '#4a3d2e'}`,
              borderRadius: '6px',
              padding: '0.6rem 1.5rem',
              color: allReady ? '#c9a84c' : '#6a5d4a',
              cursor: allReady ? 'pointer' : 'not-allowed',
              fontSize: '0.9rem',
              fontFamily: 'Georgia, serif',
            }}
          >
            Start Game
          </button>
        )}
      </div>

      <p style={{ fontSize: '0.7rem', color: '#6a5d4a', fontStyle: 'italic', textAlign: 'center' }}>
        {isHost
          ? 'You are the host. Select a class and start when all players are ready.'
          : 'Select a class and ready up. The host will start the game.'}
      </p>
    </main>
  );
}
