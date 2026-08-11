/**
 * =============================================================================
 * OverworldView.tsx — Main Overworld UI Component
 * =============================================================================
 *
 * The primary view when a player is connected to the overworld server.
 * Shows the overworld canvas with players, a minimap indicator, party info,
 * and dungeon entrance prompts.
 * =============================================================================
 */
'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { useOverworldSocket } from '@/hooks/useOverworldSocket';
import { useOverworldInput } from '@/hooks/useOverworldInput';
import { OverworldMessage, OwMessageTypes, OwPlayerState, OwDungeonEntranceData, OwWorldObjectData, OwLandmarkData, OwPartyUpdatePayload } from '@/lib/overworld-messages';
import { OverworldGameMap, decodeOverworldMap } from '@/lib/overworld-map';
import OverworldCanvas from './OverworldCanvas';
import OverworldChat from './OverworldChat';
import P2POverlay from './P2POverlay';
import { useP2POverworld } from '@/hooks/useP2POverworld';

interface OverworldViewProps {
  playerName: string;
  onDisconnect: () => void;
  onEnterDungeon?: (data: { hostAddress: string; seed: number; scenario: string }) => void;
}

export default function OverworldView({ playerName, onDisconnect, onEnterDungeon }: OverworldViewProps) {
  const [map, setMap] = useState<OverworldGameMap | null>(null);
  const [players, setPlayers] = useState<OwPlayerState[]>([]);
  const [dungeonEntrances, setDungeonEntrances] = useState<OwDungeonEntranceData[]>([]);
  const [worldObjects, setWorldObjects] = useState<OwWorldObjectData[]>([]);
  const [landmarks, setLandmarks] = useState<OwLandmarkData[]>([]);
  const [party, setParty] = useState<OwPartyUpdatePayload | null>(null);
  const [nearbyEntrance, setNearbyEntrance] = useState<OwDungeonEntranceData | null>(null);
  const [pendingInvite, setPendingInvite] = useState<{ partyId: string; inviterName: string } | null>(null);
  const [chatFocused, setChatFocused] = useState(false);

  const playersRef = useRef<Map<string, OwPlayerState>>(new Map());

  // P2P mesh state (from local game server)
  const p2p = useP2POverworld();

  const ws = useOverworldSocket({ playerName });

  const input = useOverworldInput({
    send: ws.send,
    map,
    active: (ws.status === 'connected' || map !== null) && !chatFocused,
    worldObjects,
  });

  // Load overworld map from local server REST API (P2P mode)
  useEffect(() => {
    if (map) return; // Already loaded

    // Set player name on local server so it gets broadcast to peers
    fetch('/api/p2p/name', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: playerName }),
    }).catch(() => {});

    const loadMap = async () => {
      try {
        const res = await fetch('/api/p2p/map');
        if (res.ok) {
          const data = await res.json();
          if (data && data.tilesBase64) {
            const decoded = decodeOverworldMap(data);
            setMap(decoded);
            setDungeonEntrances(data.dungeonEntrances || []);
            setWorldObjects(data.worldObjects || []);
            setLandmarks(data.landmarks || []);
            // Set spawn position
            const spawnX = data.spawnPoint?.x ? data.spawnPoint.x + 0.5 : 100.5;
            const spawnY = data.spawnPoint?.y ? data.spawnPoint.y + 0.5 : 180.5;
            input.setInitialPosition(spawnX, spawnY);
            console.log(`[Overworld] Map loaded via REST: ${data.width}x${data.height}, spawn at (${spawnX}, ${spawnY})`);
          }
        }
      } catch (e) {
        console.warn('[Overworld] Failed to load map from /api/p2p/map, will retry...', e);
        // Retry in 2 seconds
        setTimeout(loadMap, 2000);
      }
    };
    loadMap();
  }, [map]);

  // Handle incoming messages
  useEffect(() => {
    const unsub = ws.onMessage((message: OverworldMessage) => {
      switch (message.type) {
        case OwMessageTypes.MapData:
          if (message.mapData) {
            const decoded = decodeOverworldMap(message.mapData);
            setMap(decoded);
            setDungeonEntrances(message.mapData.dungeonEntrances);
            setWorldObjects(message.mapData.worldObjects);
            setLandmarks(message.mapData.landmarks);
            input.setInitialPosition(message.mapData.spawnX, message.mapData.spawnY);
          }
          break;

        case OwMessageTypes.PlayerJoined:
          if (message.playerJoined) {
            const p = message.playerJoined;
            playersRef.current.set(p.playerId, {
              id: p.playerId,
              name: p.playerName,
              x: p.x,
              y: p.y,
              velocityX: 0,
              velocityY: 0,
              status: 'exploring',
              isPartyLeader: false,
            });
            setPlayers(Array.from(playersRef.current.values()));
          }
          break;

        case OwMessageTypes.PlayerLeft:
          if (message.playerLeft) {
            playersRef.current.delete(message.playerLeft.playerId);
            setPlayers(Array.from(playersRef.current.values()));
          }
          break;

        case OwMessageTypes.WorldState:
          if (message.worldState) {
            // Update other player positions (not local player — we use prediction for that)
            for (const ps of message.worldState.players) {
              if (ps.id !== ws.playerId) {
                playersRef.current.set(ps.id, ps);
              }
            }
            setPlayers(Array.from(playersRef.current.values()));

            // Reconcile local player prediction only when server confirms our inputs
            if (message.worldState.lastProcessedInput != null && ws.playerId) {
              const localState = message.worldState.players.find(p => p.id === ws.playerId);
              if (localState) {
                input.reconcile(localState.x, localState.y, message.worldState.lastProcessedInput);
              }
            }
          }
          break;

        case OwMessageTypes.PartyInvite:
          if (message.partyInvite) {
            setPendingInvite({
              partyId: message.partyInvite.partyId,
              inviterName: message.partyInvite.inviterName,
            });
            // Auto-dismiss after 15s
            setTimeout(() => setPendingInvite(null), 15000);
          }
          break;

        case OwMessageTypes.PartyUpdate:
          if (message.partyUpdate) {
            setParty(message.partyUpdate);
            if (message.partyUpdate.event === 'disbanded') {
              setParty(null);
            }
          }
          break;

        case OwMessageTypes.DungeonConnect:
          if (message.dungeonConnect && onEnterDungeon) {
            onEnterDungeon({
              hostAddress: message.dungeonConnect.hostAddress,
              seed: message.dungeonConnect.seed,
              scenario: message.dungeonConnect.scenario,
            });
          }
          break;
      }
    });
    return unsub;
  }, [ws, input, onEnterDungeon]);

  // Auto-connect on mount
  useEffect(() => {
    ws.connect();
    return () => ws.disconnect();
  }, []);

  // Check if near a dungeon entrance
  useEffect(() => {
    const pos = input.position;
    const nearby = dungeonEntrances.find(e => {
      const dist = Math.sqrt((e.x - pos.x) ** 2 + (e.y - pos.y) ** 2);
      return dist < 2.5;
    });
    setNearbyEntrance(nearby || null);
  }, [input.position, dungeonEntrances]);

  // Sync local player position to P2P mesh (via local server)
  useEffect(() => {
    const pos = input.position;
    if (pos.x !== 0 || pos.y !== 0) {
      p2p.updatePosition(pos.x, pos.y, 0, 0);
    }
  }, [input.position, p2p]);

  // Set initial position from P2P data if we haven't received map spawn yet
  useEffect(() => {
    if (input.position.x === 0 && input.position.y === 0 && p2p.status) {
      // Find our own player in the P2P player list
      const localP2P = p2p.players.find(p => p.id === p2p.status?.peerId);
      if (localP2P && (localP2P.x !== 0 || localP2P.y !== 0)) {
        input.setInitialPosition(localP2P.x, localP2P.y);
      }
    }
  }, [p2p.players, p2p.status, input]);

  // Merge: use P2P players for remote peers, local prediction for our player
  // p2p.players includes our local player from the server — override with predicted position
  const localId = p2p.status?.peerId ?? ws.playerId;
  const displayPlayers = (p2p.players.length > 0 ? p2p.players : players).map(p => {
    if (localId && p.id === localId) {
      return { ...p, x: input.position.x, y: input.position.y };
    }
    return p;
  });

  const handleInvitePlayer = useCallback((targetId: string) => {
    // Send the target player's ID in the inviterId field (server expects it there)
    ws.send({
      type: OwMessageTypes.PartyInvite,
      partyInvite: { partyId: '', inviterId: targetId, inviterName: playerName },
    });
  }, [ws, playerName]);

  const handleAcceptInvite = useCallback(() => {
    if (!pendingInvite) return;
    ws.send({
      type: OwMessageTypes.PartyResponse,
      partyResponse: { partyId: pendingInvite.partyId, accepted: true },
    });
    setPendingInvite(null);
  }, [ws, pendingInvite]);

  const handleDeclineInvite = useCallback(() => {
    if (!pendingInvite) return;
    ws.send({
      type: OwMessageTypes.PartyResponse,
      partyResponse: { partyId: pendingInvite.partyId, accepted: false },
    });
    setPendingInvite(null);
  }, [ws, pendingInvite]);

  if (!map) {
    return (
      <div style={{
        display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center',
        height: '100vh', background: '#0d0f07', color: '#c9a84c', fontFamily: 'Georgia, serif',
      }}>
        <h2 style={{ marginBottom: '1rem' }}>Entering the Overworld...</h2>
        <p style={{ color: '#6a5d4a', fontSize: '0.9rem' }}>
          Loading world data...
        </p>
      </div>
    );
  }

  return (
    <div style={{ position: 'relative', width: '100vw', height: '100vh', overflow: 'hidden', background: '#0d0f07' }}>
      {/* Main canvas */}
      <OverworldCanvas
        map={map}
        players={displayPlayers}
        localPlayerId={p2p.status?.peerId ?? ws.playerId}
        dungeonEntrances={dungeonEntrances}
        worldObjects={worldObjects}
        landmarks={landmarks}
        width={typeof window !== 'undefined' ? window.innerWidth : 1280}
        height={typeof window !== 'undefined' ? window.innerHeight : 720}
        onPlayerClick={handleInvitePlayer}
      />

      {/* Top-left: Player info & ping */}
      <div style={{
        position: 'absolute', top: 12, left: 12, padding: '8px 12px',
        background: 'rgba(13, 15, 7, 0.85)', border: '1px solid #3a3520',
        borderRadius: 4, color: '#e8dcc8', fontSize: '0.75rem',
      }}>
        <div style={{ color: '#c9a84c', fontWeight: 'bold' }}>{playerName}</div>
        <div style={{ color: '#6a5d4a', marginTop: 2 }}>Ping: {ws.latency}ms</div>
        <div style={{ color: '#6a5d4a' }}>Players: {players.filter(p => p.status !== 'in_dungeon').length}</div>
      </div>

      {/* Party panel (top-right) */}
      {party && (
        <div style={{
          position: 'absolute', top: 12, right: 12, padding: '8px 12px',
          background: 'rgba(13, 15, 7, 0.85)', border: '1px solid #2a4a2a',
          borderRadius: 4, color: '#e8dcc8', fontSize: '0.75rem', minWidth: 140,
        }}>
          <div style={{ color: '#4a8c3f', fontWeight: 'bold', marginBottom: 4 }}>Party</div>
          {party.members.map(m => (
            <div key={m.id} style={{ display: 'flex', alignItems: 'center', gap: 4, marginBottom: 2 }}>
              {m.isLeader && <span style={{ color: '#ffd700' }}>★</span>}
              <span style={{ color: m.id === ws.playerId ? '#c9a84c' : '#9a9080' }}>{m.name}</span>
            </div>
          ))}
        </div>
      )}

      {/* Dungeon entrance prompt */}
      {nearbyEntrance && (
        <div style={{
          position: 'absolute', bottom: 80, left: '50%', transform: 'translateX(-50%)',
          padding: '10px 20px', background: 'rgba(60, 20, 20, 0.9)',
          border: '1px solid #c9a84c', borderRadius: 6, color: '#e8dcc8',
          textAlign: 'center', fontSize: '0.85rem',
        }}>
          <div style={{ color: '#c9a84c', fontWeight: 'bold' }}>{nearbyEntrance.name}</div>
          <div style={{ color: '#9a8b74', marginTop: 4 }}>Press <strong>E</strong> to enter</div>
        </div>
      )}

      {/* Party invite popup */}
      {pendingInvite && (
        <div style={{
          position: 'absolute', top: '30%', left: '50%', transform: 'translate(-50%, -50%)',
          padding: '16px 24px', background: 'rgba(13, 15, 7, 0.95)',
          border: '1px solid #4a8c3f', borderRadius: 8, color: '#e8dcc8',
          textAlign: 'center',
        }}>
          <div style={{ marginBottom: 8 }}>
            <strong style={{ color: '#c9a84c' }}>{pendingInvite.inviterName}</strong> invites you to a party
          </div>
          <div style={{ display: 'flex', gap: 8, justifyContent: 'center' }}>
            <button onClick={handleAcceptInvite} style={{
              padding: '6px 16px', background: '#2a4a2a', border: '1px solid #4a8c3f',
              borderRadius: 4, color: '#4a8c3f', cursor: 'pointer',
            }}>Accept</button>
            <button onClick={handleDeclineInvite} style={{
              padding: '6px 16px', background: '#4a2a2a', border: '1px solid #8c3f3f',
              borderRadius: 4, color: '#8c3f3f', cursor: 'pointer',
            }}>Decline</button>
          </div>
        </div>
      )}

      {/* Chat */}
      <OverworldChat
        onFocusChange={(focused) => setChatFocused(focused)}
      />

      {/* Bottom-left: Controls hint */}
      <div style={{
        position: 'absolute', bottom: 12, left: 12, padding: '6px 10px',
        background: 'rgba(13, 15, 7, 0.7)', borderRadius: 4,
        color: '#6a5d4a', fontSize: '0.65rem',
      }}>
        WASD: Move | E: Interact | Scroll: Zoom | Click player: Invite | Enter: Chat
      </div>

      {/* P2P Mesh overlay (shard info, Glyph, peer count) */}
      <P2POverlay
        status={p2p.status}
        shard={p2p.shard}
        glyph={p2p.glyph}
        onGlyphConnect={p2p.connectViaGlyph}
      />

      {/* Disconnect button */}
      <button onClick={onDisconnect} style={{
        position: 'absolute', bottom: 12, right: 12, padding: '6px 12px',
        background: 'rgba(80, 30, 30, 0.8)', border: '1px solid #6a3030',
        borderRadius: 4, color: '#a85050', cursor: 'pointer', fontSize: '0.7rem',
      }}>
        Disconnect
      </button>
    </div>
  );
}
