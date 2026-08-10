'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { useWebSocket } from '@/hooks/useWebSocket';
import { useGameInput } from '@/hooks/useGameInput';
import { GameMessage, MessageTypes, EntityState, SessionInfoPayload, GameEventPayload, createMessage } from '@/lib/messages';
import { GameMap, decodeMap } from '@/lib/map';
import GameCanvas from '@/components/GameCanvas';
import GameHUD from '@/components/GameHUD';
import Lobby from '@/components/Lobby';

export default function Home() {
  const [playerName, setPlayerName] = useState('');
  const [messages, setMessages] = useState<string[]>([]);
  const [chatInput, setChatInput] = useState('');
  const [gameMap, setGameMap] = useState<GameMap | null>(null);
  const [entities, setEntities] = useState<EntityState[]>([]);
  const [inGame, setInGame] = useState(false);
  const [chatFocused, setChatFocused] = useState(false);
  const [sessionInfo, setSessionInfo] = useState<SessionInfoPayload | null>(null);
  const [gameEvents, setGameEvents] = useState<GameEventPayload[]>([]);

  const ws = useWebSocket({
    playerName: playerName || 'Anonymous',
    autoReconnect: true,
  });

  // Game input system with client-side prediction
  const gameInput = useGameInput({
    send: ws.send,
    map: gameMap,
    active: inGame && ws.status === 'connected' && !chatFocused,
  });

  const addLog = useCallback((msg: string) => {
    setMessages(prev => [...prev.slice(-50), `[${new Date().toLocaleTimeString()}] ${msg}`]);
  }, []);

  // Handle reconciliation — update predicted position when server confirms inputs
  const handleGameState = useCallback((message: GameMessage) => {
    if (!message.gameState || !ws.playerId) return;

    const { entities: serverEntities, lastProcessedInput } = message.gameState;

    // Find local player entity in the update
    const localEntityId = `player_${ws.playerId}`;
    const localEntity = serverEntities.find(e => e.id === localEntityId);

    if (localEntity && lastProcessedInput != null) {
      // Reconcile prediction with server state
      gameInput.reconcile(localEntity.x, localEntity.y, lastProcessedInput);
    }

    // Update entities state — for local player, use predicted position
    setEntities(prev => {
      const entityMap = new Map(prev.map(e => [e.id, e]));
      for (const entity of serverEntities) {
        if (entity.id === localEntityId) {
          // Use predicted position for local player instead of server position
          const predicted = gameInput.getPredictedPosition();
          entityMap.set(entity.id, {
            ...entity,
            x: predicted.x,
            y: predicted.y,
          });
        } else {
          entityMap.set(entity.id, entity);
        }
      }
      // Remove dead entities
      for (const [id, entity] of entityMap) {
        if (!entity.isAlive) entityMap.delete(id);
      }
      return Array.from(entityMap.values());
    });
  }, [ws.playerId, gameInput]);

  useEffect(() => {
    const unsubscribe = ws.onMessage((message: GameMessage) => {
      switch (message.type) {
        case MessageTypes.PlayerJoined:
          addLog(`Player joined: ${message.playerJoined?.playerName}`);
          break;
        case MessageTypes.PlayerLeft:
          addLog(`Player left: ${message.playerLeft?.playerId}`);
          break;
        case MessageTypes.Chat:
          addLog(`${message.chat?.senderName}: ${message.chat?.message}`);
          break;
        case MessageTypes.MapData:
          if (message.mapData) {
            const map = decodeMap(message.mapData);
            setGameMap(map);
            setInGame(true);
            addLog(`Map loaded: ${map.width}x${map.height} (seed: ${map.seed})`);
          }
          break;
        case MessageTypes.GameState:
          handleGameState(message);
          break;
        case MessageTypes.SessionInfo:
          if (message.sessionInfo) {
            setSessionInfo(message.sessionInfo);
            // Transition to game when session state changes to playing
            if (message.sessionInfo.state === 'playing' && !inGame) {
              // Map will arrive separately via MapData message
            }
          }
          break;
        case MessageTypes.GameEvent:
          if (message.gameEvent) {
            setGameEvents(prev => [...prev.slice(-10), message.gameEvent!]);
            if (message.gameEvent.message) {
              addLog(message.gameEvent.message);
            }
            // Auto-clear events after 4 seconds
            setTimeout(() => {
              setGameEvents(prev => prev.slice(1));
            }, 4000);
          }
          break;
        case MessageTypes.Error:
          addLog(`Error: ${message.error?.message}`);
          break;
      }
    });
    return unsubscribe;
  }, [ws, addLog, handleGameState]);

  // Set initial position when first entity data arrives for local player
  useEffect(() => {
    if (!ws.playerId) return;
    const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
    if (localEntity) {
      gameInput.setPosition(localEntity.x, localEntity.y);
    }
  }, [ws.playerId, entities.length > 0]); // Only run when first entities arrive

  const handleConnect = () => {
    if (!playerName.trim()) return;
    ws.connect();
  };

  const handleDisconnect = () => {
    ws.disconnect();
    setInGame(false);
    setGameMap(null);
    setEntities([]);
    setSessionInfo(null);
  };

  const handleSendChat = () => {
    if (!chatInput.trim() || !ws.playerId) return;
    const msg = createMessage(MessageTypes.Chat, {
      senderId: ws.playerId,
      senderName: playerName,
      message: chatInput,
      timestamp: Date.now(),
    });
    ws.send(msg);
    addLog(`You: ${chatInput}`);
    setChatInput('');
  };

  // If in lobby, show the lobby UI
  if (ws.status === 'connected' && sessionInfo && sessionInfo.state === 'lobby' && !inGame) {
    return <Lobby sessionInfo={sessionInfo} localPlayerId={ws.playerId} send={ws.send} />;
  }

  // If in game, show the full HUD
  if (inGame && ws.status === 'connected') {
    return (
      <GameHUD
        localPlayerId={ws.playerId}
        entities={entities}
        sessionInfo={sessionInfo}
        events={gameEvents}
        latency={ws.latency}
        chatMessages={messages}
        chatInput={chatInput}
        onChatInputChange={setChatInput}
        onChatSend={handleSendChat}
        onChatFocus={() => setChatFocused(true)}
        onChatBlur={() => setChatFocused(false)}
        onDisconnect={handleDisconnect}
      >
        <GameCanvas
          map={gameMap}
          entities={entities}
          localPlayerId={ws.playerId}
          width={800}
          height={600}
          tileSize={24}
        />
      </GameHUD>
    );
  }

  // Connection screen
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
        fontSize: '3rem',
        fontFamily: "'Georgia', serif",
        color: '#c9a84c',
        textShadow: '0 0 20px rgba(201, 168, 76, 0.3)',
        letterSpacing: '0.3em',
      }}>
        CARCOSA
      </h1>
      <p style={{ color: '#9a8b74', fontStyle: 'italic' }}>
        The King in Yellow
      </p>

      <div style={{
        background: '#2a2218',
        border: '1px solid #4a3d2e',
        borderRadius: '8px',
        padding: '1.5rem',
        width: '100%',
        maxWidth: '400px',
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '1rem' }}>
          <div style={{
            width: '10px',
            height: '10px',
            borderRadius: '50%',
            background: ws.status === 'connected' ? '#4a8c3f' :
                       ws.status === 'connecting' ? '#c9a84c' :
                       ws.status === 'error' ? '#a83232' : '#6a5d4a',
          }} />
          <span style={{ color: '#e8dcc8', fontSize: '0.9rem' }}>
            {ws.status === 'connected' ? 'Connected' :
             ws.status === 'connecting' ? 'Connecting...' :
             ws.status === 'error' ? 'Error' : 'Ready to connect'}
          </span>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
          <input
            type="text"
            placeholder="Enter your name..."
            value={playerName}
            onChange={(e) => setPlayerName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleConnect()}
            style={{
              background: '#1a1410',
              border: '1px solid #4a3d2e',
              borderRadius: '4px',
              padding: '0.6rem 0.75rem',
              color: '#e8dcc8',
              fontSize: '1rem',
              outline: 'none',
            }}
          />
          <button
            onClick={handleConnect}
            disabled={!playerName.trim() || ws.status === 'connecting'}
            style={{
              background: '#4a3d2e',
              border: '1px solid #c9a84c',
              borderRadius: '4px',
              padding: '0.6rem 1rem',
              color: '#c9a84c',
              cursor: 'pointer',
              fontSize: '1rem',
              fontFamily: 'Georgia, serif',
            }}
          >
            Enter Carcosa
          </button>
        </div>
      </div>

      <p style={{ fontSize: '0.8rem', color: '#6a5d4a', fontStyle: 'italic' }}>
        Along the shore the cloud waves break...
      </p>
    </main>
  );
}
