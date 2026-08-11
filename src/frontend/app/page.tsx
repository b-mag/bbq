'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { useWebSocket } from '@/hooks/useWebSocket';
import { useGameInput } from '@/hooks/useGameInput';
import { GameMessage, MessageTypes, EntityState, SessionInfoPayload, GameEventPayload, createMessage } from '@/lib/messages';
import { GameMap, decodeMap } from '@/lib/map';
import { VisualEffectsSystem } from '@/lib/engine/effects';
import GameCanvas from '@/components/GameCanvas';
import GameHUD from '@/components/GameHUD';
import Lobby from '@/components/Lobby';

export default function Home() {
  const [playerName, setPlayerName] = useState('');
  const [messages, setMessages] = useState<string[]>([]);
  const [gameMap, setGameMap] = useState<GameMap | null>(null);
  const [entities, setEntities] = useState<EntityState[]>([]);
  const [inGame, setInGame] = useState(false);
  const [chatFocused, setChatFocused] = useState(false);
  const [sessionInfo, setSessionInfo] = useState<SessionInfoPayload | null>(null);
  const [gameEvents, setGameEvents] = useState<GameEventPayload[]>([]);
  const effectsSystemRef = useRef<VisualEffectsSystem | null>(null);

  // Spectate state — activated when local player dies
  const [isSpectating, setIsSpectating] = useState(false);
  const [spectateTargetId, setSpectateTargetId] = useState<string | null>(null);
  const deathTimerRef = useRef<NodeJS.Timeout | null>(null);

  // Online mode — detected by checking matchmaking service availability
  const [isOnline, setIsOnline] = useState(false);
  const [availableSessions, setAvailableSessions] = useState<Array<{
    sessionId: string; hostAddress: string; playerCount: number;
    maxPlayers: number; state: string; scenario: string; currentWave: number;
  }>>([]);
  const [showSessionBrowser, setShowSessionBrowser] = useState(false);

  // Check if matchmaking service is available (polls every 5 seconds)
  useEffect(() => {
    const check = () => {
      fetch('/api/matchmaking-status')
        .then(res => res.json())
        .then((data: { isOnline: boolean }) => setIsOnline(data.isOnline))
        .catch(() => setIsOnline(false));
    };
    check();
    const interval = setInterval(check, 5000);
    return () => clearInterval(interval);
  }, []);

  // Fetch available sessions when session browser is opened
  useEffect(() => {
    if (!showSessionBrowser) return;
    const load = () => {
      fetch('/api/available-sessions')
        .then(res => res.json())
        .then(setAvailableSessions)
        .catch(() => setAvailableSessions([]));
    };
    load();
    const interval = setInterval(load, 3000); // Refresh every 3s
    return () => clearInterval(interval);
  }, [showSessionBrowser]);

  const ws = useWebSocket({
    playerName: playerName || 'Anonymous',
    autoReconnect: true,
  });

  // Game input system with client-side prediction
  const gameInput = useGameInput({
    send: ws.send,
    map: gameMap,
    active: inGame && ws.status === 'connected' && !chatFocused && !isSpectating,
    onFire: useCallback((x: number, y: number, angle: number) => {
      const fx = effectsSystemRef.current;
      if (!fx) return;
      // Determine player class from entities to pick the right effect
      const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
      const playerClass = localEntity?.subType || '';
      if (playerClass === 'surgeon') {
        // Surgeon: show slash arc (melee weapon)
        fx.addSlashArc(x, y, angle);
      } else {
        // Gangster/Detective: show muzzle flash (ranged weapon)
        const flashX = x + Math.cos(angle) * 0.4;
        const flashY = y + Math.sin(angle) * 0.4;
        fx.addMuzzleFlash(flashX, flashY, angle, '#ffc832', playerClass === 'detective' ? 1.5 : 0.8);
      }
    }, [entities, ws.playerId]),
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
            // Trigger visual effects based on event type
            const fx = effectsSystemRef.current;
            if (fx && message.gameEvent.x !== undefined && message.gameEvent.y !== undefined) {
              const evt = message.gameEvent;
              if (evt.event === 'damage' && evt.x && evt.y) {
                // Impact spark at damage location
                fx.addImpactSpark(evt.x, evt.y);
              }
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

  // Detect local player death → start spectate timer
  useEffect(() => {
    if (!ws.playerId || !inGame) return;
    const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
    if (!localEntity) return;

    if (!localEntity.isAlive && !isSpectating && !deathTimerRef.current) {
      // Player just died — wait 3 seconds then enter spectate mode
      deathTimerRef.current = setTimeout(() => {
        // Find first alive teammate to spectate
        const aliveTeammates = entities.filter(
          e => e.entityType === 'player' && e.isAlive && e.id !== `player_${ws.playerId}`
        );
        if (aliveTeammates.length > 0) {
          setSpectateTargetId(aliveTeammates[0].id);
          setIsSpectating(true);
        }
        deathTimerRef.current = null;
      }, 3000);
    }

    // If player is revived, cancel spectate
    if (localEntity.isAlive && (isSpectating || deathTimerRef.current)) {
      if (deathTimerRef.current) {
        clearTimeout(deathTimerRef.current);
        deathTimerRef.current = null;
      }
      setIsSpectating(false);
      setSpectateTargetId(null);
    }
  }, [entities, ws.playerId, inGame, isSpectating]);

  // Tab key cycles spectate target between alive teammates
  useEffect(() => {
    if (!isSpectating) return;

    const handleTab = (e: KeyboardEvent) => {
      if (e.key === 'Tab') {
        e.preventDefault();
        const aliveTeammates = entities.filter(
          e => e.entityType === 'player' && e.isAlive && e.id !== `player_${ws.playerId}`
        );
        if (aliveTeammates.length === 0) return;

        // Find current target index and cycle to next
        const currentIndex = aliveTeammates.findIndex(e => e.id === spectateTargetId);
        const nextIndex = (currentIndex + 1) % aliveTeammates.length;
        setSpectateTargetId(aliveTeammates[nextIndex].id);
      }
    };

    window.addEventListener('keydown', handleTab);
    return () => window.removeEventListener('keydown', handleTab);
  }, [isSpectating, entities, spectateTargetId, ws.playerId]);

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
    setIsSpectating(false);
    setSpectateTargetId(null);
    if (deathTimerRef.current) {
      clearTimeout(deathTimerRef.current);
      deathTimerRef.current = null;
    }
  };

  const handleSendChat = (message: string) => {
    if (!message.trim() || !ws.playerId) return;
    const msg = createMessage(MessageTypes.Chat, {
      senderId: ws.playerId,
      senderName: playerName,
      message: message,
      timestamp: Date.now(),
    });
    ws.send(msg);
    addLog(`You: ${message}`);
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
        onChatSend={handleSendChat}
        onChatFocus={() => setChatFocused(true)}
        onChatBlur={() => setChatFocused(false)}
        onDisconnect={handleDisconnect}
        isSpectating={isSpectating}
        spectateTargetName={
          spectateTargetId
            ? entities.find(e => e.id === spectateTargetId)?.subType || 'Teammate'
            : undefined
        }
      >
        <GameCanvas
          map={gameMap}
          entities={entities}
          localPlayerId={ws.playerId}
          spectateTargetId={spectateTargetId}
          width={800}
          height={600}
          tileSize={24}
          onCanvasReady={(canvas) => gameInput.inputHandler.setCanvas(canvas)}
          onEffectsReady={(fx) => { effectsSystemRef.current = fx; }}
          inputHandler={gameInput.inputHandler}
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
        Cooperative Survival RPG
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

        {/* Matchmaking status indicator */}
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: '0.4rem',
          padding: '0.3rem 0.6rem',
          borderRadius: '4px',
          background: isOnline ? 'rgba(74, 140, 63, 0.1)' : 'rgba(106, 93, 74, 0.1)',
          border: `1px solid ${isOnline ? 'rgba(74, 140, 63, 0.3)' : 'rgba(106, 93, 74, 0.3)'}`,
        }}>
          <div style={{
            width: '6px',
            height: '6px',
            borderRadius: '50%',
            background: isOnline ? '#4a8c3f' : '#6a5d4a',
          }} />
          <span style={{ fontSize: '0.7rem', color: isOnline ? '#4a8c3f' : '#6a5d4a' }}>
            {isOnline ? 'Online — Multiplayer available' : 'Offline — Standalone mode'}
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

      {/* Join a Game button (only shown when matchmaking is online) */}
      {isOnline && (
        <button
          onClick={() => setShowSessionBrowser(true)}
          style={{
            background: '#2a3a2a',
            border: '1px solid #4a8c3f',
            borderRadius: '4px',
            padding: '0.6rem 1.5rem',
            color: '#4a8c3f',
            cursor: 'pointer',
            fontSize: '0.9rem',
            fontFamily: 'Georgia, serif',
            width: '100%',
            maxWidth: '400px',
          }}
        >
          Join a Game
        </button>
      )}

      {/* Session browser modal */}
      {showSessionBrowser && (
        <div style={{
          position: 'fixed',
          inset: 0,
          background: 'rgba(0,0,0,0.7)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          zIndex: 1000,
        }} onClick={() => setShowSessionBrowser(false)}>
          <div onClick={(e) => e.stopPropagation()} style={{
            background: '#1a1410',
            border: '1px solid #4a3d2e',
            borderRadius: '8px',
            padding: '1.5rem',
            width: '500px',
            maxHeight: '400px',
            overflow: 'auto',
          }}>
            <h2 style={{ color: '#c9a84c', fontSize: '1.1rem', marginBottom: '1rem', fontFamily: 'Georgia, serif' }}>
              Available Games
            </h2>
            {availableSessions.length === 0 ? (
              <p style={{ color: '#6a5d4a', textAlign: 'center', padding: '2rem 0' }}>
                No games found. Try hosting your own!
              </p>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '0.5rem' }}>
                {availableSessions.filter(s => s.playerCount < s.maxPlayers).map(session => (
                  <div key={session.sessionId} style={{
                    background: '#2a2218',
                    border: '1px solid #4a3d2e',
                    borderRadius: '4px',
                    padding: '0.75rem',
                    display: 'flex',
                    justifyContent: 'space-between',
                    alignItems: 'center',
                    cursor: 'pointer',
                  }}
                  onClick={() => {
                    // Connect to the remote session's host
                    // For now, open in a new tab pointing at the host
                    const protocol = window.location.protocol === 'https:' ? 'https:' : 'http:';
                    window.open(`${protocol}//${session.hostAddress}`, '_blank');
                    setShowSessionBrowser(false);
                  }}
                  >
                    <div>
                      <div style={{ color: '#e8dcc8', fontSize: '0.85rem' }}>
                        {session.scenario.charAt(0).toUpperCase() + session.scenario.slice(1)}
                        <span style={{
                          marginLeft: '0.5rem',
                          padding: '1px 6px',
                          borderRadius: '8px',
                          fontSize: '0.65rem',
                          background: session.state === 'lobby' ? 'rgba(201, 168, 76, 0.2)' : 'rgba(74, 140, 63, 0.2)',
                          color: session.state === 'lobby' ? '#c9a84c' : '#4a8c3f',
                        }}>
                          {session.state}
                        </span>
                      </div>
                      <div style={{ color: '#6a5d4a', fontSize: '0.7rem', marginTop: '2px' }}>
                        {session.hostAddress} • Wave {session.currentWave || '—'}
                      </div>
                    </div>
                    <div style={{ color: '#9a8b74', fontSize: '0.8rem' }}>
                      {session.playerCount}/{session.maxPlayers}
                    </div>
                  </div>
                ))}
              </div>
            )}
            <button
              onClick={() => setShowSessionBrowser(false)}
              style={{
                marginTop: '1rem',
                background: '#2a2218',
                border: '1px solid #4a3d2e',
                borderRadius: '4px',
                padding: '0.4rem 1rem',
                color: '#9a8b74',
                cursor: 'pointer',
                fontSize: '0.8rem',
                width: '100%',
              }}
            >
              Close
            </button>
          </div>
        </div>
      )}

      <p style={{ fontSize: '0.8rem', color: '#6a5d4a', fontStyle: 'italic' }}>
        Cooperative Survival RPG
      </p>

      <div style={{
        marginTop: '0.5rem',
        padding: '0.5rem 1rem',
        border: '1px solid #3a3020',
        borderRadius: '4px',
        background: 'rgba(42, 34, 24, 0.5)',
        textAlign: 'center',
      }}>
        <span style={{ color: '#6a5d4a', fontSize: '0.7rem', fontStyle: 'italic' }}>
          Coming soon: Scenario &mdash; Carcosa
        </span>
      </div>
    </main>
  );
}
