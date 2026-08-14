'use client';

import { useState, useEffect, useCallback, useRef } from 'react';
import { useWebSocket } from '@/hooks/useWebSocket';
import { useGameInput } from '@/hooks/useGameInput';
import { GameMessage, MessageTypes, EntityState, SessionInfoPayload, GameEventPayload, createMessage } from '@/lib/messages';
import { GameMap, decodeMap } from '@/lib/map';
import { VisualEffectsSystem } from '@/lib/engine/effects';
import GameCanvas from '@/components/GameCanvas';
import GameHUD from '@/components/GameHUD';
import OverworldView from '@/components/OverworldView';

/**
 * Application state machine:
 *   'connect' → Player enters name
 *   'overworld' → Connected to overworld server, exploring shared world
 *   'dungeon' → In an instanced dungeon (peer-hosted by party leader)
 */
type AppState = 'connect' | 'overworld' | 'dungeon';

export default function Home() {
  const [appState, setAppState] = useState<AppState>('connect');
  const [playerName, setPlayerName] = useState('');
  const [bootstrapping, setBootstrapping] = useState(true);

  // Dungeon state (used when in 'dungeon' mode)
  const [dungeonInfo, setDungeonInfo] = useState<{ hostAddress: string; seed: number; scenario: string } | null>(null);
  const [messages, setMessages] = useState<string[]>([]);
  const [gameMap, setGameMap] = useState<GameMap | null>(null);
  const [entities, setEntities] = useState<EntityState[]>([]);
  const [chatFocused, setChatFocused] = useState(false);
  const [sessionInfo, setSessionInfo] = useState<SessionInfoPayload | null>(null);
  const [gameEvents, setGameEvents] = useState<GameEventPayload[]>([]);
  const effectsSystemRef = useRef<VisualEffectsSystem | null>(null);
  const [isSpectating, setIsSpectating] = useState(false);
  const [spectateTargetId, setSpectateTargetId] = useState<string | null>(null);
  const deathTimerRef = useRef<NodeJS.Timeout | null>(null);

  // Dungeon WebSocket (connects to party leader's Carcosa.Server)
  const ws = useWebSocket({
    playerName: playerName || 'Anonymous',
    serverUrl: dungeonInfo?.hostAddress,
    autoReconnect: false,
  });

  // Game input for dungeon mode
  const gameInput = useGameInput({
    send: ws.send,
    map: gameMap,
    active: appState === 'dungeon' && ws.status === 'connected' && !chatFocused && !isSpectating,
    onFire: useCallback((x: number, y: number, angle: number) => {
      const fx = effectsSystemRef.current;
      if (!fx) return;
      const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
      const playerClass = localEntity?.subType || '';
      if (playerClass === 'surgeon') {
        fx.addSlashArc(x, y, angle);
      } else {
        const flashX = x + Math.cos(angle) * 0.4;
        const flashY = y + Math.sin(angle) * 0.4;
        fx.addMuzzleFlash(flashX, flashY, angle, '#ffc832', playerClass === 'detective' ? 1.5 : 0.8);
      }
    }, [entities, ws.playerId]),
  });

  const addLog = useCallback((msg: string) => {
    setMessages(prev => [...prev.slice(-50), `[${new Date().toLocaleTimeString()}] ${msg}`]);
  }, []);

  // Handle dungeon game state messages
  const handleGameState = useCallback((message: GameMessage) => {
    if (!message.gameState || !ws.playerId) return;
    const { entities: serverEntities, lastProcessedInput } = message.gameState;
    const localEntityId = `player_${ws.playerId}`;
    const localEntity = serverEntities.find(e => e.id === localEntityId);

    if (localEntity && lastProcessedInput != null) {
      gameInput.reconcile(localEntity.x, localEntity.y, lastProcessedInput);
    }

    setEntities(prev => {
      const entityMap = new Map(prev.map(e => [e.id, e]));
      for (const entity of serverEntities) {
        if (entity.id === localEntityId) {
          const predicted = gameInput.getPredictedPosition();
          entityMap.set(entity.id, { ...entity, x: predicted.x, y: predicted.y });
        } else {
          entityMap.set(entity.id, entity);
        }
      }
      for (const [id, entity] of entityMap) {
        if (!entity.isAlive) entityMap.delete(id);
      }
      return Array.from(entityMap.values());
    });
  }, [ws.playerId, gameInput]);

  // Dungeon message handler
  useEffect(() => {
    if (appState !== 'dungeon') return;

    const unsub = ws.onMessage((message: GameMessage) => {
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
            addLog(`Dungeon loaded: ${map.width}x${map.height}`);
          }
          break;
        case MessageTypes.GameState:
          handleGameState(message);
          break;
        case MessageTypes.SessionInfo:
          if (message.sessionInfo) setSessionInfo(message.sessionInfo);
          break;
        case MessageTypes.GameEvent:
          if (message.gameEvent) {
            setGameEvents(prev => [...prev.slice(-10), message.gameEvent!]);
            if (message.gameEvent.message) addLog(message.gameEvent.message);
            const fx = effectsSystemRef.current;
            if (fx && message.gameEvent.x && message.gameEvent.y) {
              if (message.gameEvent.event === 'damage') {
                fx.addImpactSpark(message.gameEvent.x, message.gameEvent.y);
              }
            }
            setTimeout(() => setGameEvents(prev => prev.slice(1)), 4000);

            // Return to overworld on game over/victory
            if (message.gameEvent.event === 'game_over' || message.gameEvent.event === 'victory') {
              setTimeout(() => handleReturnToOverworld(), 5000);
            }
          }
          break;
      }
    });
    return unsub;
  }, [appState, ws, addLog, handleGameState]);

  // Set initial position when dungeon starts
  useEffect(() => {
    if (appState !== 'dungeon' || !ws.playerId) return;
    const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
    if (localEntity) {
      gameInput.setPosition(localEntity.x, localEntity.y);
    }
  }, [appState, ws.playerId, entities.length > 0]);

  // Spectate logic (same as before)
  useEffect(() => {
    if (appState !== 'dungeon' || !ws.playerId) return;
    const localEntity = entities.find(e => e.id === `player_${ws.playerId}`);
    if (!localEntity) return;
    if (!localEntity.isAlive && !isSpectating && !deathTimerRef.current) {
      deathTimerRef.current = setTimeout(() => {
        const alive = entities.filter(e => e.entityType === 'player' && e.isAlive && e.id !== `player_${ws.playerId}`);
        if (alive.length > 0) {
          setSpectateTargetId(alive[0].id);
          setIsSpectating(true);
        }
        deathTimerRef.current = null;
      }, 3000);
    }
    if (localEntity.isAlive && (isSpectating || deathTimerRef.current)) {
      if (deathTimerRef.current) { clearTimeout(deathTimerRef.current); deathTimerRef.current = null; }
      setIsSpectating(false);
      setSpectateTargetId(null);
    }
  }, [entities, ws.playerId, appState, isSpectating]);

  // Handle dungeon disconnect — return to overworld if connection drops
  useEffect(() => {
    if (appState !== 'dungeon') return;
    if (ws.status === 'disconnected' && gameMap) {
      // Connection dropped after we had loaded the map — return to overworld
      console.log('[Dungeon] Connection lost, returning to overworld');
      setTimeout(() => handleReturnToOverworld(), 1000);
    }
  }, [appState, ws.status, gameMap]);

  // First-run name gate: skip connect screen if the save already has a name
  useEffect(() => {
    const bootstrap = async () => {
      try {
        const res = await fetch('/api/gameplay/bootstrap');
        if (res.ok) {
          const data = await res.json();
          if (!data.needsName && data.displayName) {
            setPlayerName(data.displayName);
            setAppState('overworld');
          }
        }
      } catch {
        // Show connect screen if bootstrap fails
      } finally {
        setBootstrapping(false);
      }
    };
    bootstrap();
  }, []);

  // Enter overworld from connect screen
  const handleEnterOverworld = async () => {
    if (!playerName.trim()) return;
    try {
      await fetch('/api/p2p/name', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: playerName.trim() }),
      });
    } catch { /* proceed even if name POST fails */ }
    setAppState('overworld');
  };

  // Enter dungeon from overworld
  const handleEnterDungeon = (data: { hostAddress: string; seed: number; scenario: string }) => {
    setDungeonInfo(data);
    setAppState('dungeon');
    // Connect to dungeon host
    setTimeout(() => ws.connect(), 100);
  };

  // Return to overworld from dungeon
  const handleReturnToOverworld = () => {
    ws.disconnect();
    setAppState('overworld');
    setDungeonInfo(null);
    setGameMap(null);
    setEntities([]);
    setSessionInfo(null);
    setIsSpectating(false);
    setSpectateTargetId(null);
  };

  // Full disconnect
  const handleDisconnect = () => {
    ws.disconnect();
    setAppState('connect');
    setDungeonInfo(null);
    setGameMap(null);
    setEntities([]);
    setSessionInfo(null);
  };

  const handleSendChat = (message: string) => {
    if (!message.trim() || !ws.playerId) return;
    const msg = createMessage(MessageTypes.Chat, {
      senderId: ws.playerId, senderName: playerName, message, timestamp: Date.now(),
    });
    ws.send(msg);
    addLog(`You: ${message}`);
  };

  // --- RENDER ---

  if (bootstrapping) {
    return (
      <div style={{
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        height: '100vh',
        background: 'radial-gradient(ellipse at center, #2a2218 0%, #1a1410 70%, #0d0a07 100%)',
        color: '#c9a84c', fontFamily: 'Georgia, serif',
      }}>
        <h1 style={{
          fontSize: '2rem', letterSpacing: '0.3em',
          textShadow: '0 0 20px rgba(201, 168, 76, 0.3)',
        }}>
          CARCOSA
        </h1>
        <p style={{ color: '#6a5d4a', marginTop: 8, fontStyle: 'italic' }}>Stirring...</p>
      </div>
    );
  }

  // Overworld state
  if (appState === 'overworld') {
    return (
      <OverworldView
        playerName={playerName}
        onDisconnect={handleDisconnect}
        onEnterDungeon={handleEnterDungeon}
      />
    );
  }

  // Dungeon state
  if (appState === 'dungeon' && gameMap && ws.status === 'connected') {
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
        onDisconnect={handleReturnToOverworld}
        isSpectating={isSpectating}
        spectateTargetName={
          spectateTargetId ? entities.find(e => e.id === spectateTargetId)?.subType || 'Teammate' : undefined
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

  // Dungeon loading state
  if (appState === 'dungeon') {
    return (
      <div style={{
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        height: '100vh', background: '#0d0a07', color: '#c9a84c', fontFamily: 'Georgia, serif',
      }}>
        <h2>Entering Dungeon...</h2>
        <p style={{ color: '#6a5d4a', marginTop: 8 }}>
          {ws.status === 'connecting' ? 'Connecting to dungeon host...' : 'Loading dungeon...'}
        </p>
        <button onClick={handleReturnToOverworld} style={{
          marginTop: 20, padding: '8px 16px', background: '#4a2a2a', border: '1px solid #6a3030',
          borderRadius: 4, color: '#a85050', cursor: 'pointer',
        }}>
          Return to Overworld
        </button>
      </div>
    );
  }

  // Connect screen (initial state)
  return (
    <main style={{
      display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
      height: '100vh', width: '100vw',
      background: 'radial-gradient(ellipse at center, #2a2218 0%, #1a1410 70%, #0d0a07 100%)',
      padding: '2rem', gap: '1.5rem',
    }}>
      <h1 style={{
        fontSize: '3rem', fontFamily: "'Georgia', serif", color: '#c9a84c',
        textShadow: '0 0 20px rgba(201, 168, 76, 0.3)', letterSpacing: '0.3em',
      }}>
        CARCOSA
      </h1>
      <p style={{ color: '#9a8b74', fontStyle: 'italic' }}>
        Along the shore the cloud waves break...
      </p>

      <div style={{
        background: '#2a2218', border: '1px solid #4a3d2e', borderRadius: '8px',
        padding: '1.5rem', width: '100%', maxWidth: '400px',
      }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '0.75rem' }}>
          <input
            type="text"
            placeholder="Enter your name..."
            value={playerName}
            onChange={(e) => setPlayerName(e.target.value)}
            onKeyDown={(e) => e.key === 'Enter' && handleEnterOverworld()}
            style={{
              background: '#1a1410', border: '1px solid #4a3d2e', borderRadius: '4px',
              padding: '0.6rem 0.75rem', color: '#e8dcc8', fontSize: '1rem', outline: 'none',
            }}
          />
          <button
            onClick={handleEnterOverworld}
            disabled={!playerName.trim()}
            style={{
              background: '#4a3d2e', border: '1px solid #c9a84c', borderRadius: '4px',
              padding: '0.6rem 1rem', color: '#c9a84c', cursor: 'pointer',
              fontSize: '1rem', fontFamily: 'Georgia, serif',
            }}
          >
            Enter Carcosa
          </button>
        </div>
      </div>

      <p style={{ fontSize: '0.75rem', color: '#6a5d4a', fontStyle: 'italic', textAlign: 'center', maxWidth: 400 }}>
        Strange is the night where black stars rise,<br />
        And strange moons circle through the skies,<br />
        But stranger still is Lost Carcosa.
      </p>
    </main>
  );
}
